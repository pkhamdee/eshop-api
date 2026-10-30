namespace Eshop.Api.ThirdParty;

/// <summary>SMS port: sends a text message to a Thai mobile number. Tests replace it with a fake.</summary>
internal interface ISmsSender
{
    Task SendAsync(string mobileNumber, string text, CancellationToken cancellationToken);
}

/// <summary>SMS adapter for thirdparty-mock (<c>POST /sms/messages</c>), which shows messages in its inbox page.</summary>
internal sealed class ThirdPartyMockSmsSender(HttpClient http) : ISmsSender
{
    public async Task SendAsync(string mobileNumber, string text, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("sms/messages", new { to = mobileNumber, text }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
