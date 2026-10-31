namespace Eshop.Api.Tests;

/// <summary>
/// Every response tells browsers not to sniff its content type (ZAP rule 10021, scripts/gates/dast.sh), refusals and
/// errors included.
/// </summary>
public sealed class SecurityHeadersTests(EshopApiFactory api)
{
    [Theory]
    [InlineData("/api/branches")] // 200 JSON
    [InlineData("/api/auth/me")] // 401
    [InlineData("/api/products/999999")] // 404
    [InlineData("/healthz/ready")] // probe, plain text
    public async Task Responses_forbid_content_type_sniffing(string path)
    {
        var response = await api.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var values), $"no header on {path}");
        Assert.Equal(["nosniff"], values);
    }
}
