using Eshop.Data;
using Eshop.Domain.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.BackOffice;

/// <summary>
/// Staff set each Product Category's Flat Rate and longest Term. The catalog and calculator read them on every
/// request, so a change shows immediately; submitted Applications and Offers keep the terms they were given.
/// </summary>
internal static class ProductCategoryAdministration
{
    /// <summary>Upper bound on the monthly Flat Rate, a guard against typing 1.5 for 1.5%.</summary>
    public const decimal MaxFlatRate = 0.10m;

    public static void MapProductCategoryAdministration(this RouteGroupBuilder backOffice)
    {
        var categories = backOffice.MapGroup("/product-categories");
        categories.MapGet("/", ListProductCategories);
        categories.MapPut("/{code}", UpdateSettings);
    }

    private static async Task<List<BackOfficeProductCategoryResponse>> ListProductCategories(
        EshopDbContext db, CancellationToken cancellationToken) =>
        await db.ProductCategories
            .OrderBy(c => c.SortOrder)
            .Select(c => new BackOfficeProductCategoryResponse(
                c.Code, c.Name, c.FlatRate, c.LongestTerm, c.Products.Count(p => !p.IsRetired)))
            .ToListAsync(cancellationToken);

    private static async Task<Results<Ok<BackOfficeProductCategoryResponse>, NotFound, ValidationProblem>> UpdateSettings(
        string code, ProductCategorySettingsRequest request, EshopDbContext db, CancellationToken cancellationToken)
    {
        var category = await db.ProductCategories.SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (category is null)
        {
            return TypedResults.NotFound();
        }

        var errors = BackOfficeEndpoints.Errors();
        errors.Require(
            request.FlatRate is > 0m and <= MaxFlatRate && decimal.Round(request.FlatRate, 4) == request.FlatRate,
            "flatRate",
            "อัตราดอกเบี้ยคงที่ต้องมากกว่า 0% และไม่เกิน 10% ต่อเดือน (ทศนิยมไม่เกิน 2 ตำแหน่ง)");
        errors.Require(
            InstallmentPlan.Terms.Contains(request.LongestTerm),
            "longestTerm",
            $"จำนวนงวดสูงสุดต้องเป็น {string.Join(", ", InstallmentPlan.Terms)}");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        category.FlatRate = request.FlatRate;
        category.LongestTerm = request.LongestTerm;
        await db.SaveChangesAsync(cancellationToken);

        var activeProducts = await db.Products.CountAsync(p => p.ProductCategoryId == category.Id && !p.IsRetired, cancellationToken);
        return TypedResults.Ok(new BackOfficeProductCategoryResponse(
            category.Code, category.Name, category.FlatRate, category.LongestTerm, activeProducts));
    }
}

// FlatRate: monthly, as a fraction (0.015 = 1.5% per month). LongestTerm: one of 6, 10, 12, 18, 24.
internal sealed record ProductCategorySettingsRequest(decimal FlatRate, int LongestTerm);

internal sealed record BackOfficeProductCategoryResponse(string Code, string Name, decimal FlatRate, int LongestTerm, int ActiveProducts);
