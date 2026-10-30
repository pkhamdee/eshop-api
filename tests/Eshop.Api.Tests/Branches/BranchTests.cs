using System.Net.Http.Json;

namespace Eshop.Api.Tests.Branches;

public sealed class BranchTests(EshopApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task Visitor_sees_the_demo_Branches_for_Store_Pickup()
    {
        var branches = await _client.GetFromJsonAsync<List<BranchResponse>>(
            "/api/branches", TestContext.Current.CancellationToken);

        Assert.NotNull(branches);
        Assert.Contains(branches, b => b.Code == "bkk-ladprao" && b.Province == "กรุงเทพมหานคร");
        Assert.Contains(branches, b => b.Province != "กรุงเทพมหานคร");
        Assert.All(branches, b => Assert.False(string.IsNullOrWhiteSpace(b.Address)));
    }

    private sealed record BranchResponse(string Code, string Name, string Address, string Province);
}
