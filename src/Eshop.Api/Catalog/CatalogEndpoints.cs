using Eshop.Data;
using Eshop.Domain.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Catalog;

/// <summary>Public catalog: Product Categories and Products. No sign-in needed.</summary>
internal static class CatalogEndpoints
{
    // Product photos are served by eshop-node-api through the Gateway; eshop-api stores only the image ID.
    private const string PublicImagePath = "/images/public/";

    private static string? PublicImageUrl(string? imageId) => imageId is null ? null : PublicImagePath + imageId;

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        var categories = api.MapGroup("/product-categories").WithTags("Catalog");
        categories.MapGet("/", ListProductCategories);
        categories.MapGet("/{code}/products", ListProductsInCategory);

        var products = api.MapGroup("/products").WithTags("Catalog");
        products.MapGet("/{id:int}", GetProduct);
        products.MapGet("/{id:int}/installment-plans", ListInstallmentPlans);
        products.MapGet("/{id:int}/installment-plans/{term:int}", CalculateInstallment);

        return api;
    }

    private static async Task<List<ProductCategoryResponse>> ListProductCategories(
        EshopDbContext db, CancellationToken cancellationToken) =>
        await db.ProductCategories
            .OrderBy(c => c.SortOrder)
            .Select(c => new ProductCategoryResponse(c.Code, c.Name, c.FlatRate, c.LongestTerm))
            .ToListAsync(cancellationToken);

    private static async Task<Results<Ok<List<ProductSummaryResponse>>, NotFound>> ListProductsInCategory(
        string code, EshopDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.ProductCategories.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var products = await db.Products
            .Where(p => p.ProductCategory.Code == code && !p.IsRetired)
            .OrderBy(p => p.Price)
            .Include(p => p.ProductCategory)
            .ToListAsync(cancellationToken);

        // "from ฿X / month": the lowest Installment offered with no Down Payment (null if none is offered).
        return TypedResults.Ok(products
            .Select(p => new ProductSummaryResponse(
                p.Id,
                p.Name,
                p.Price,
                p.ProductCategory.Code,
                InstallmentPlan.Offered(p.Price, p.ProductCategory, 0m).Select(plan => (decimal?)plan.Installment).Min(),
                PublicImageUrl(p.ImageId)))
            .ToList());
    }

    private static async Task<Results<Ok<ProductDetailResponse>, NotFound>> GetProduct(
        int id, EshopDbContext db, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Where(p => p.Id == id && !p.IsRetired)
            .Select(p => new ProductDetailResponse(
                p.Id,
                p.Name,
                p.Description,
                p.Price,
                new ProductCategoryResponse(
                    p.ProductCategory.Code, p.ProductCategory.Name, p.ProductCategory.FlatRate, p.ProductCategory.LongestTerm),
                p.ImageId == null ? null : PublicImagePath + p.ImageId))
            .SingleOrDefaultAsync(cancellationToken);

        return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
    }

    private static async Task<Results<Ok<InstallmentPlansResponse>, NotFound, ValidationProblem>> ListInstallmentPlans(
        int id, EshopDbContext db, CancellationToken cancellationToken, decimal downPayment = 0m)
    {
        var product = await FindProduct(id, db, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        if (!InstallmentPlan.IsValidDownPayment(product.Price, downPayment))
        {
            return InvalidDownPayment();
        }

        var plans = InstallmentPlan.Offered(product.Price, product.ProductCategory, downPayment)
            .Select(p => new TermPlanResponse(p.Term, p.Installment, p.TotalPayable))
            .ToList();

        return TypedResults.Ok(new InstallmentPlansResponse(product.Price, downPayment, plans));
    }

    private static async Task<Results<Ok<InstallmentPlanResponse>, NotFound, ValidationProblem>> CalculateInstallment(
        int id, int term, EshopDbContext db, CancellationToken cancellationToken, decimal downPayment = 0m)
    {
        var product = await FindProduct(id, db, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        if (!InstallmentPlan.IsValidDownPayment(product.Price, downPayment))
        {
            return InvalidDownPayment();
        }

        var plan = InstallmentPlan.For(product.Price, product.ProductCategory, term, downPayment);
        if (plan is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["term"] = [$"Term {term} is not offered for this Product and Down Payment."],
            });
        }

        return TypedResults.Ok(new InstallmentPlanResponse(
            plan.Price, plan.DownPayment, plan.Term, plan.Installment, plan.TotalPayable));
    }

    private static Task<Product?> FindProduct(int id, EshopDbContext db, CancellationToken cancellationToken) =>
        db.Products
            .Include(p => p.ProductCategory)
            .SingleOrDefaultAsync(p => p.Id == id && !p.IsRetired, cancellationToken);

    private static ValidationProblem InvalidDownPayment() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["downPayment"] = ["Down Payment can't be negative and must be less than the price."],
        });
}

// FlatRate: monthly Flat Rate as a fraction (0.015 = 1.5% per month).
internal sealed record ProductCategoryResponse(string Code, string Name, decimal FlatRate, int LongestTerm);

// FromInstallment: the lowest Installment offered with no Down Payment, for "from ฿X / month".
// ImageUrl: same-origin path of the Product photo (served by eshop-node-api), or null when there is none.
internal sealed record ProductSummaryResponse(
    int Id, string Name, decimal Price, string ProductCategoryCode, decimal? FromInstallment, string? ImageUrl);

internal sealed record ProductDetailResponse(
    int Id, string Name, string Description, decimal Price, ProductCategoryResponse ProductCategory, string? ImageUrl);

internal sealed record TermPlanResponse(int Term, decimal Installment, decimal TotalPayable);

// TotalPayable = Down Payment + Installment x Term.
internal sealed record InstallmentPlansResponse(decimal Price, decimal DownPayment, List<TermPlanResponse> Plans);

internal sealed record InstallmentPlanResponse(
    decimal Price, decimal DownPayment, int Term, decimal Installment, decimal TotalPayable);
