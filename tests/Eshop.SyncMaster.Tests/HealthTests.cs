using System.Net;

namespace Eshop.SyncMaster.Tests;

public sealed class HealthTests(SyncMasterFactory sync)
{
    private readonly HttpClient _client = sync.CreateClient();

    [Theory]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")] // both PostgreSQL and the Enterprise database answer
    public async Task Kubernetes_probes_report_healthy(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
