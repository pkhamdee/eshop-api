using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.Catalog;

// Installment = ceil((price - Down Payment) x (1 + Flat Rate x Term) / Term), in whole baht.
// Expected values below are worked by hand from the demo catalog (spec #1, "Domain rules").
public sealed class InstallmentTests(EshopApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Visitor_sees_every_offered_Term_with_its_Installment_and_total_payable()
    {
        // iPhone 17 128GB: ฿29,900, mobile phone 1.5% / month, longest Term 24.
        var id = await ProductId("mobile-phone", "iPhone 17 128GB");

        var offered = await _client.GetFromJsonAsync<InstallmentPlans>($"/api/products/{id}/installment-plans", Ct);

        Assert.NotNull(offered);
        Assert.Equal((29_900m, 0m), (offered.Price, offered.DownPayment));
        Assert.Equal(
            [
                new Plan(6, 5_432m, 32_592m),    // 29,900 x 1.09 / 6  = 5,431.83
                new Plan(10, 3_439m, 34_390m),   // 29,900 x 1.15 / 10 = 3,438.50
                new Plan(12, 2_941m, 35_292m),   // 29,900 x 1.18 / 12 = 2,940.17
                new Plan(18, 2_110m, 37_980m),   // 29,900 x 1.27 / 18 = 2,109.61
                new Plan(24, 1_695m, 40_680m),   // 29,900 x 1.36 / 24 = 1,694.33
            ],
            offered.Plans);
    }

    [Fact]
    public async Task A_Down_Payment_reduces_the_financed_amount_and_counts_towards_the_total_payable()
    {
        var id = await ProductId("mobile-phone", "iPhone 17 128GB");

        var offered = await _client.GetFromJsonAsync<InstallmentPlans>(
            $"/api/products/{id}/installment-plans?downPayment=5900", Ct);

        // Financed 24,000 x 1.18 / 12 = 2,360 exactly; total = 5,900 + 12 x 2,360.
        Assert.Equal(new Plan(12, 2_360m, 34_220m), offered!.Plans.Single(p => p.Term == 12));
    }

    [Fact]
    public async Task Terms_beyond_the_Product_Categorys_longest_Term_are_not_offered()
    {
        // Samsung fridge: ฿15,990, electrical appliance 1.25% / month, longest Term 18.
        var id = await ProductId("electrical-appliance", "ตู้เย็น 2 ประตู Samsung 13.8 คิว");

        var offered = await _client.GetFromJsonAsync<InstallmentPlans>($"/api/products/{id}/installment-plans", Ct);

        Assert.Equal([6, 10, 12, 18], offered!.Plans.Select(p => p.Term));
        Assert.Equal(new Plan(18, 1_089m, 19_602m), offered.Plans[^1]); // 15,990 x 1.225 / 18 = 1,088.21
    }

    [Fact]
    public async Task Terms_whose_Installment_would_fall_under_500_baht_are_not_offered()
    {
        // iPad (A16): ฿12,900 at 1.5%. Down 10,150 -> financed 2,750.
        // Term 6: 2,750 x 1.09 / 6 = 499.58, rounded up to exactly ฿500 -> offered.
        // Term 10: 2,750 x 1.15 / 10 = 316.25 -> not offered (nor any longer Term).
        var id = await ProductId("ipad", "iPad (A16) Wi-Fi 128GB");

        var offered = await _client.GetFromJsonAsync<InstallmentPlans>(
            $"/api/products/{id}/installment-plans?downPayment=10150", Ct);

        Assert.Equal([new Plan(6, 500m, 13_150m)], offered!.Plans);
    }

    [Fact]
    public async Task Visitor_calculates_the_Installment_for_one_chosen_Term()
    {
        var id = await ProductId("electric-motorcycle", "Yadea E8S Pro");

        // ฿42,900 at 1.0%, down 2,900 -> financed 40,000 x 1.24 / 24 = 2,066.67 -> 2,067.
        var plan = await _client.GetFromJsonAsync<InstallmentPlan>(
            $"/api/products/{id}/installment-plans/24?downPayment=2900", Ct);

        Assert.Equal(new InstallmentPlan(42_900m, 2_900m, 24, 2_067m, 52_508m), plan);
    }

    [Theory]
    [InlineData("mobile-phone", "iPhone 17 128GB", 9, 0)]                        // not a Term eShop offers
    [InlineData("electrical-appliance", "ตู้เย็น 2 ประตู Samsung 13.8 คิว", 24, 0)] // beyond the category's longest Term
    [InlineData("ipad", "iPad (A16) Wi-Fi 128GB", 10, 10_150)]                    // Installment under ฿500
    [InlineData("mobile-phone", "iPhone 17 128GB", 12, -1)]                       // negative Down Payment
    [InlineData("mobile-phone", "iPhone 17 128GB", 12, 29_900)]                   // Down Payment reaches the price
    public async Task A_plan_that_is_not_offered_is_rejected(string category, string name, int term, int downPayment)
    {
        var id = await ProductId(category, name);

        var response = await _client.GetAsync(
            $"/api/products/{id}/installment-plans/{term}?downPayment={downPayment}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29_900)]
    [InlineData(35_000)]
    public async Task Listing_plans_with_an_impossible_Down_Payment_is_rejected(int downPayment)
    {
        var id = await ProductId("mobile-phone", "iPhone 17 128GB");

        var response = await _client.GetAsync($"/api/products/{id}/installment-plans?downPayment={downPayment}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Plans_for_an_unknown_Product_are_not_found()
    {
        var response = await _client.GetAsync("/api/products/999999/installment-plans", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listing_cards_show_the_lowest_offered_Installment()
    {
        var phones = await _client.GetFromJsonAsync<List<ListedProduct>>("/api/product-categories/mobile-phone/products", Ct);
        var furniture = await _client.GetFromJsonAsync<List<ListedProduct>>("/api/product-categories/furniture/products", Ct);

        Assert.Equal(1_695m, phones!.Single(p => p.Name == "iPhone 17 128GB").FromInstallment);    // Term 24
        Assert.Equal(612m, furniture!.Single(p => p.Name == "ตู้เสื้อผ้า 4 บานประตู").FromInstallment); // 8,990 x 1.225 / 18 = 611.82
    }

    private async Task<int> ProductId(string category, string name)
    {
        var products = await _client.GetFromJsonAsync<List<ListedProduct>>($"/api/product-categories/{category}/products", Ct);
        return products!.Single(p => p.Name == name).Id;
    }

    private sealed record ListedProduct(int Id, string Name, decimal? FromInstallment);

    private sealed record Plan(int Term, decimal Installment, decimal TotalPayable);

    private sealed record InstallmentPlans(decimal Price, decimal DownPayment, List<Plan> Plans);

    private sealed record InstallmentPlan(decimal Price, decimal DownPayment, int Term, decimal Installment, decimal TotalPayable);
}
