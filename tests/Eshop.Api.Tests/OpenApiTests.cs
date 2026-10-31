using System.Net;
using System.Text.Json;

namespace Eshop.Api.Tests;

/// <summary>
/// The OpenAPI description drives the ZAP API scan (scripts/gates/dast.sh). It is served only where a setting asks
/// for it (the temporary copy), never by default.
/// </summary>
public sealed class OpenApiTests(EshopApiFactory api)
{
    [Fact]
    public async Task The_api_description_is_not_served_by_default()
    {
        var response = await api.CreateClient().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task When_enabled_the_api_description_lists_the_endpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var enabled = api.WithWebHostBuilder(builder => builder.UseSetting("OpenApi:Enabled", "true"));

        var response = await enabled.CreateClient().GetAsync("/openapi/v1.json", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();
        Assert.Contains("/api/products/{id}", paths);
        Assert.Contains("/api/auth/sms-code", paths);
        Assert.Contains("/api/back-office/auctions", paths);
    }
}
