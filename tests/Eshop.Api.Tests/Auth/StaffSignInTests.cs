using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Eshop.Api.Tests.Auth;

public sealed class StaffSignInTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staff_signs_in_with_username_and_password_and_gets_a_Staff_token()
    {
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/staff/sign-in",
            new { username = EshopApiFactory.StaffUsername, password = EshopApiFactory.StaffPassword },
            Ct);

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Ct);
        Assert.Equal("Staff", session!.Role);
        Assert.Equal(EshopApiFactory.StaffUsername, session.Username);
        Assert.True(session.ExpiresAt > api.Clock.GetUtcNow());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", Ct);
        Assert.Equal("Staff", me!.Role);
        Assert.Equal(EshopApiFactory.StaffUsername, me.Username);
        Assert.Null(me.CustomerId);
    }

    [Theory]
    [InlineData(EshopApiFactory.StaffUsername, "wrong-password")]
    [InlineData("nobody", EshopApiFactory.StaffPassword)]
    [InlineData("", "")]
    public async Task Wrong_username_or_password_is_refused_without_saying_which(string username, string password)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/staff/sign-in", new { username, password }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>(Ct);
        Assert.Equal("credentials-invalid", problem!.Error);
    }

    [Fact]
    public async Task A_Customer_token_is_not_a_Staff_token()
    {
        var customer = await api.CreateCustomerClientAsync();

        var me = await customer.GetFromJsonAsync<MeResponse>("/api/auth/me", Ct);

        Assert.Equal("Customer", me!.Role);
        Assert.Null(me.Username);
    }

    [Fact]
    public async Task Tokens_are_issued_by_eshop_api_for_eshop()
    {
        var response = await api.CreateClient().PostAsJsonAsync(
            "/api/auth/staff/sign-in",
            new { username = EshopApiFactory.StaffUsername, password = EshopApiFactory.StaffPassword },
            Ct);

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Ct);
        var payload = JwtPayload(session!.Token);
        Assert.Equal("eshop-api", payload.GetProperty("iss").GetString());
        Assert.Equal("eshop", payload.GetProperty("aud").GetString());
    }

    private static JsonElement JwtPayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment))).RootElement;
    }

    private sealed record SessionResponse(string Token, DateTimeOffset ExpiresAt, string Role, string? Username);

    private sealed record MeResponse(string Role, string? MobileNumber, int? CustomerId, string? Username);

    private sealed record Problem(string? Error);
}
