using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.BackOffice;

public sealed class ProductAdministrationTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staff_adds_a_Product_and_visitors_see_it_in_the_catalog_with_its_installments()
    {
        var staff = await api.CreateStaffClientAsync();
        var name = $"Xiaomi 16 {Guid.NewGuid():N}";

        var response = await staff.PostAsJsonAsync("/api/back-office/products", new
        {
            productCategoryCode = "mobile-phone",
            name,
            description = "จอ 6.4 นิ้ว",
            price = 12_000m,
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BackOfficeProduct>(Ct);
        Assert.Equal(name, created!.Name);
        Assert.False(created.IsRetired);

        var visitor = api.CreateClient();
        var catalog = await visitor.GetFromJsonAsync<List<ProductSummary>>("/api/product-categories/mobile-phone/products", Ct);
        var listed = Assert.Single(catalog!, p => p.Id == created.Id);
        Assert.Equal(12_000m, listed.Price);
        // mobile phone: 1.5% / month, longest Term 24 → ⌈12,000 × 1.36 ÷ 24⌉ = 680.
        Assert.Equal(680m, listed.FromInstallment);
    }

    [Fact]
    public async Task Staff_edits_a_Product_and_the_calculator_uses_the_new_price()
    {
        var staff = await api.CreateStaffClientAsync();
        var product = await AddProductAsync(staff, "furniture", 10_000m);

        var response = await staff.PutAsJsonAsync($"/api/back-office/products/{product.Id}", new
        {
            productCategoryCode = "furniture",
            name = product.Name + " (ใหม่)",
            description = "ปรับราคา",
            price = 9_000m,
            imageId = (string?)null,
        }, Ct);

        response.EnsureSuccessStatusCode();
        var visitor = api.CreateClient();
        var detail = await visitor.GetFromJsonAsync<ProductDetail>($"/api/products/{product.Id}", Ct);
        Assert.Equal(9_000m, detail!.Price);
        Assert.Equal(product.Name + " (ใหม่)", detail.Name);
        // furniture: 1.25% / month → 6 months: ⌈9,000 × 1.075 ÷ 6⌉ = 1,613.
        var plan = await visitor.GetFromJsonAsync<Plan>($"/api/products/{product.Id}/installment-plans/6", Ct);
        Assert.Equal(1_613m, plan!.Installment);
    }

    [Fact]
    public async Task A_retired_Product_disappears_from_the_catalog_but_Staff_still_see_it()
    {
        var staff = await api.CreateStaffClientAsync();
        var product = await AddProductAsync(staff, "ipad", 15_000m);

        var retire = await staff.PostAsync($"/api/back-office/products/{product.Id}/retire", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        var visitor = api.CreateClient();
        var catalog = await visitor.GetFromJsonAsync<List<ProductSummary>>("/api/product-categories/ipad/products", Ct);
        Assert.DoesNotContain(catalog!, p => p.Id == product.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);

        var backOffice = await staff.GetFromJsonAsync<List<BackOfficeProduct>>("/api/back-office/products", Ct);
        Assert.True(Assert.Single(backOffice!, p => p.Id == product.Id).IsRetired);

        // Retiring by mistake can be undone.
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsync($"/api/back-office/products/{product.Id}/reinstate", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Staff_uploads_a_Product_photo_through_the_image_service()
    {
        var staff = await api.CreateStaffClientAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        using var body = new ByteArrayContent(png);
        body.Headers.ContentType = new("image/png");

        var upload = await staff.PostAsync("/api/back-office/product-photos", body, Ct);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var photo = await upload.Content.ReadFromJsonAsync<Photo>(Ct);
        Assert.Equal(png, api.Images.PublicImage(photo!.ImageId));
        Assert.Equal($"/images/public/{photo.ImageId}", photo.ImageUrl);

        var product = await AddProductAsync(staff, "mobile-phone", 20_000m, photo.ImageId);
        var detail = await api.CreateClient().GetFromJsonAsync<ProductDetail>($"/api/products/{product.Id}", Ct);
        Assert.Equal(photo.ImageUrl, detail!.ImageUrl);
    }

    // SVG is refused too: it can carry script and would be served from our own origin.
    [Theory]
    [InlineData("text/plain", "hello")]
    [InlineData("image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    public async Task A_photo_that_is_not_a_JPEG_PNG_or_WebP_is_refused(string contentType, string content)
    {
        var staff = await api.CreateStaffClientAsync();
        using var body = new StringContent(content, System.Text.Encoding.UTF8, contentType);

        var upload = await staff.PostAsync("/api/back-office/product-photos", body, Ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, upload.StatusCode);
    }

    [Theory]
    [InlineData("mobile-phone", "", 10_000)]
    [InlineData("mobile-phone", "ชื่อ", 0)]
    [InlineData("spaceship", "ชื่อ", 10_000)]
    public async Task An_invalid_Product_is_refused(string category, string name, int price)
    {
        var staff = await api.CreateStaffClientAsync();

        var response = await staff.PostAsJsonAsync("/api/back-office/products", new
        {
            productCategoryCode = category,
            name,
            description = "",
            price,
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_or_retiring_an_unknown_Product_is_not_found()
    {
        var staff = await api.CreateStaffClientAsync();

        var edit = await staff.PutAsJsonAsync("/api/back-office/products/999999", new
        {
            productCategoryCode = "ipad",
            name = "x",
            description = "",
            price = 1_000m,
        }, Ct);
        var retire = await staff.PostAsync("/api/back-office/products/999999/retire", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, retire.StatusCode);
    }

    private static async Task<BackOfficeProduct> AddProductAsync(HttpClient staff, string category, decimal price, string? imageId = null)
    {
        var response = await staff.PostAsJsonAsync("/api/back-office/products", new
        {
            productCategoryCode = category,
            name = $"สินค้าทดสอบ {Guid.NewGuid():N}",
            description = "ทดสอบ",
            price,
            imageId,
        }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BackOfficeProduct>(Ct))!;
    }

    private sealed record BackOfficeProduct(
        int Id, string Name, string Description, decimal Price, string ProductCategoryCode, string? ImageId, string? ImageUrl, bool IsRetired);

    private sealed record ProductSummary(int Id, string Name, decimal Price, decimal? FromInstallment);

    private sealed record ProductDetail(int Id, string Name, decimal Price, string? ImageUrl);

    private sealed record Plan(int Term, decimal Installment);

    private sealed record Photo(string ImageId, string ImageUrl);
}
