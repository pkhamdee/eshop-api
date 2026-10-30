using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.Auth;

public sealed class CustomerSignInTests(EshopApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task Customer_signs_in_with_the_code_sent_by_SMS()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();

        var request = await _client.PostAsJsonAsync("/api/auth/sms-code", new { mobileNumber }, ct);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);

        var code = api.Sms.LatestCodeFor(mobileNumber);
        var verify = await _client.PostAsJsonAsync("/api/auth/sms-code/verify", new { mobileNumber, code }, ct);

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var session = await verify.Content.ReadFromJsonAsync<SessionResponse>(ct);
        Assert.NotNull(session);
        Assert.Equal("Customer", session.Role);
        Assert.Equal(mobileNumber, session.MobileNumber);
        Assert.True(session.ExpiresAt > api.Clock.GetUtcNow());

        var profile = await MeAsync(session.Token, ct);
        Assert.Equal("Customer", profile.Role);
        Assert.Equal(mobileNumber, profile.MobileNumber);
    }

    [Fact]
    public async Task The_sign_in_code_SMS_names_eShop()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();

        await RequestCodeAsync(mobileNumber, ct);

        var text = api.Sms.SentTo(mobileNumber).Last().Text;
        Assert.Contains("eShop", text);
        Assert.DoesNotContain("S Phone", text);
    }

    [Fact]
    public async Task First_sign_in_creates_the_Customer_and_later_sign_ins_reach_the_same_Customer()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();

        var first = await (await api.CreateCustomerClientAsync(mobileNumber)).GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        var second = await (await api.CreateCustomerClientAsync(mobileNumber)).GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        var someoneElse = await (await api.CreateCustomerClientAsync()).GetFromJsonAsync<MeResponse>("/api/auth/me", ct);

        Assert.NotNull(first!.CustomerId);
        Assert.Equal(first.CustomerId, second!.CustomerId);
        Assert.NotEqual(first.CustomerId, someoneElse!.CustomerId);
    }

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();
        await RequestCodeAsync(mobileNumber, ct);

        var verify = await VerifyAsync(mobileNumber, WrongCode(api.Sms.LatestCodeFor(mobileNumber)), ct);

        await AssertRefusedAsync(verify, "code-invalid", ct);
    }

    [Fact]
    public async Task After_five_wrong_codes_even_the_right_code_is_refused_until_a_new_code_is_requested()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();
        await RequestCodeAsync(mobileNumber, ct);
        var code = api.Sms.LatestCodeFor(mobileNumber);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await AssertRefusedAsync(await VerifyAsync(mobileNumber, WrongCode(code), ct), "code-invalid", ct);
        }

        await AssertRefusedAsync(await VerifyAsync(mobileNumber, WrongCode(code), ct), "code-locked", ct);
        await AssertRefusedAsync(await VerifyAsync(mobileNumber, code, ct), "code-locked", ct);

        await RequestCodeAsync(mobileNumber, ct);
        var verify = await VerifyAsync(mobileNumber, api.Sms.LatestCodeFor(mobileNumber), ct);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();
        await RequestCodeAsync(mobileNumber, ct);
        var code = api.Sms.LatestCodeFor(mobileNumber);

        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(mobileNumber, code, ct)).StatusCode);
        await AssertRefusedAsync(await VerifyAsync(mobileNumber, code, ct), "code-invalid", ct);
    }

    [Fact]
    public async Task Requesting_a_new_code_replaces_the_previous_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var mobileNumber = TestData.NewMobileNumber();
        await RequestCodeAsync(mobileNumber, ct);
        var oldCode = api.Sms.LatestCodeFor(mobileNumber);
        await RequestCodeAsync(mobileNumber, ct);
        var newCode = api.Sms.LatestCodeFor(mobileNumber);

        if (oldCode != newCode)
        {
            await AssertRefusedAsync(await VerifyAsync(mobileNumber, oldCode, ct), "code-invalid", ct);
        }

        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(mobileNumber, newCode, ct)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("081234567")]
    [InlineData("12345678901")]
    [InlineData("0212345678")]
    public async Task A_code_is_not_sent_to_something_that_is_not_a_Thai_mobile_number(string mobileNumber)
    {
        var ct = TestContext.Current.CancellationToken;

        var request = await _client.PostAsJsonAsync("/api/auth/sms-code", new { mobileNumber }, ct);

        await AssertRefusedAsync(request, "mobile-number-invalid", ct);
        Assert.Empty(api.Sms.SentTo(mobileNumber));
    }

    [Fact]
    public async Task Who_am_I_needs_a_valid_token()
    {
        var ct = TestContext.Current.CancellationToken;

        var anonymous = await _client.GetAsync("/api/auth/me", ct);
        using var forged = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        forged.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwicm9sZSI6IlN0YWZmIn0.c2lnbmF0dXJlLW5vdC12YWxpZA");
        var forgedResponse = await _client.SendAsync(forged, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
    }

    private async Task RequestCodeAsync(string mobileNumber, CancellationToken ct) =>
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await _client.PostAsJsonAsync("/api/auth/sms-code", new { mobileNumber }, ct)).StatusCode);

    private Task<HttpResponseMessage> VerifyAsync(string mobileNumber, string code, CancellationToken ct) =>
        _client.PostAsJsonAsync("/api/auth/sms-code/verify", new { mobileNumber, code }, ct);

    private async Task<MeResponse> MeAsync(string token, CancellationToken ct)
    {
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(me, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(ct))!;
    }

    internal static async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(ct);
        Assert.Equal(error, problem!.Error);
    }

    /// <summary>A 6-digit code guaranteed to differ from <paramref name="code"/>.</summary>
    internal static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private sealed record SessionResponse(string Token, DateTimeOffset ExpiresAt, string Role, string MobileNumber);

    private sealed record MeResponse(string Role, string? MobileNumber, int? CustomerId);

    private sealed record ProblemResponse(string? Error);
}
