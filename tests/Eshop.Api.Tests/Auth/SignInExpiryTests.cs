using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.Auth;

[Collection(nameof(ClockMoving))]
public sealed class SignInExpiryTests(EshopApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task A_code_expires_after_five_minutes()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();
        (await _client.PostAsJsonAsync("/api/auth/sms-code", new { mobileNumber }, ct)).EnsureSuccessStatusCode();
        var code = api.Sms.LatestCodeFor(mobileNumber);

        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var verify = await _client.PostAsJsonAsync("/api/auth/sms-code/verify", new { mobileNumber, code }, ct);

        await CustomerSignInTests.AssertRefusedAsync(verify, "code-expired", ct);
    }

    [Fact]
    public async Task A_code_still_works_just_before_it_expires()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();
        (await _client.PostAsJsonAsync("/api/auth/sms-code", new { mobileNumber }, ct)).EnsureSuccessStatusCode();
        var code = api.Sms.LatestCodeFor(mobileNumber);

        api.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        var verify = await _client.PostAsJsonAsync("/api/auth/sms-code/verify", new { mobileNumber, code }, ct);

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task A_Customer_token_stops_working_after_seven_days()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await api.CreateCustomerClientAsync();

        api.Clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/auth/me", ct)).StatusCode);

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/auth/me", ct)).StatusCode);
    }
}
