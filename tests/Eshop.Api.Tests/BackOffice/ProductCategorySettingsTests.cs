using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.BackOffice;

/// <summary>
/// Changes a shared Product Category's settings, so it runs alone: other tests calculate Installments
/// from the demo settings (mobile phone 1.5% / 24) in parallel.
/// </summary>
[Collection(nameof(ProductCategorySettings))]
public sealed class ProductCategorySettingsTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staff_change_a_Flat_Rate_and_longest_Term_and_the_calculator_follows_immediately()
    {
        var staff = await api.CreateStaffClientAsync();
        var original = (await staff.GetFromJsonAsync<List<Category>>("/api/back-office/product-categories", Ct))!
            .Single(c => c.Code == "electric-motorcycle");
        var product = await AddProductAsync(staff, "electric-motorcycle", 30_000m);

        try
        {
            var response = await staff.PutAsJsonAsync(
                "/api/back-office/product-categories/electric-motorcycle", new { flatRate = 0.02m, longestTerm = 12 }, Ct);

            response.EnsureSuccessStatusCode();
            var visitor = api.CreateClient();
            var category = (await visitor.GetFromJsonAsync<List<Category>>("/api/product-categories", Ct))!
                .Single(c => c.Code == "electric-motorcycle");
            Assert.Equal(0.02m, category.FlatRate);
            Assert.Equal(12, category.LongestTerm);

            var plans = await visitor.GetFromJsonAsync<PlanList>($"/api/products/{product.Id}/installment-plans", Ct);
            Assert.Equal([6, 10, 12], plans!.Plans.Select(p => p.Term));
            // 2% / month over 12 months: ⌈30,000 × 1.24 ÷ 12⌉ = 3,100.
            Assert.Equal(3_100m, plans.Plans.Single(p => p.Term == 12).Installment);
        }
        finally
        {
            (await staff.PutAsJsonAsync(
                "/api/back-office/product-categories/electric-motorcycle",
                new { flatRate = original.FlatRate, longestTerm = original.LongestTerm },
                Ct)).EnsureSuccessStatusCode();
        }
    }

    [Theory]
    [InlineData(0.0, 24)]
    [InlineData(-0.01, 24)]
    [InlineData(0.2, 24)]
    [InlineData(0.015, 7)]
    [InlineData(0.015, 36)]
    public async Task Settings_outside_the_rules_are_refused(double flatRate, int longestTerm)
    {
        var staff = await api.CreateStaffClientAsync();

        var response = await staff.PutAsJsonAsync(
            "/api/back-office/product-categories/furniture", new { flatRate = (decimal)flatRate, longestTerm }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_Product_Category_is_not_found()
    {
        var staff = await api.CreateStaffClientAsync();

        var response = await staff.PutAsJsonAsync(
            "/api/back-office/product-categories/spaceship", new { flatRate = 0.01m, longestTerm = 12 }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Product> AddProductAsync(HttpClient staff, string category, decimal price)
    {
        var response = await staff.PostAsJsonAsync("/api/back-office/products", new
        {
            productCategoryCode = category,
            name = $"สินค้าทดสอบ {Guid.NewGuid():N}",
            description = "ทดสอบ",
            price,
        }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Product>(Ct))!;
    }

    private sealed record Category(string Code, string Name, decimal FlatRate, int LongestTerm);

    private sealed record Product(int Id);

    private sealed record PlanList(List<Plan> Plans);

    private sealed record Plan(int Term, decimal Installment);
}

/// <summary>Tests that change shared Product Category settings run alone, after the parallel tests.</summary>
[CollectionDefinition(nameof(ProductCategorySettings), DisableParallelization = true)]
public sealed class ProductCategorySettings;
