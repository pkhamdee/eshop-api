using System.Net.Http.Json;

namespace Eshop.Api.Tests.Catalog;

public sealed class ProductCategoryTests(EshopApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task Visitor_sees_every_Product_Category_with_its_Flat_Rate_and_longest_Term()
    {
        var categories = await _client.GetFromJsonAsync<List<ProductCategoryResponse>>(
            "/api/product-categories", TestContext.Current.CancellationToken);

        Assert.NotNull(categories);
        Assert.Equal(
            [
                ("mobile-phone", 0.015m, 24),
                ("ipad", 0.015m, 24),
                ("electrical-appliance", 0.0125m, 18),
                ("furniture", 0.0125m, 18),
                ("electric-motorcycle", 0.010m, 24),
            ],
            categories.Select(c => (c.Code, c.FlatRate, c.LongestTerm)));
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    private sealed record ProductCategoryResponse(string Code, string Name, decimal FlatRate, int LongestTerm);
}
