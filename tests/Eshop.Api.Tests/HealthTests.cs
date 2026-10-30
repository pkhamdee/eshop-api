using System.Net;

namespace Eshop.Api.Tests;

public sealed class HealthTests(EshopApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    [Theory]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    public async Task Kubernetes_probes_report_healthy(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
