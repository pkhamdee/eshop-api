using System.Text.RegularExpressions;
using Eshop.Api.Images;
using Eshop.Data;
using Eshop.Domain.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.BackOffice;

/// <summary>
/// Staff add, edit and retire Products and upload Product photos. Editing a price changes the catalog and
/// calculator at once; Applications lock their own price when submitted, so they are not affected.
/// Retired Products stay in the database (Applications may refer to them) but leave the public catalog.
/// </summary>
internal static partial class ProductAdministration
{
    public static void MapProductAdministration(this RouteGroupBuilder backOffice)
    {
        var products = backOffice.MapGroup("/products");
        products.MapGet("/", ListProducts);
        products.MapPost("/", AddProduct);
        products.MapPut("/{id:int}", EditProduct);
        products.MapPost("/{id:int}/retire", (int id, EshopDbContext db, CancellationToken ct) => SetRetired(id, true, db, ct));
        products.MapPost("/{id:int}/reinstate", (int id, EshopDbContext db, CancellationToken ct) => SetRetired(id, false, db, ct));

        backOffice.MapPost("/product-photos", UploadPhoto);
    }

    // Every Product, retired ones included, by Product Category then name.
    private static async Task<List<BackOfficeProductResponse>> ListProducts(EshopDbContext db, CancellationToken cancellationToken)
    {
        var products = await db.Products
            .Include(p => p.ProductCategory)
            .OrderBy(p => p.ProductCategory.SortOrder).ThenBy(p => p.IsRetired).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return products.Select(ToResponse).ToList();
    }

    private static async Task<Results<Created<BackOfficeProductResponse>, ValidationProblem>> AddProduct(
        ProductRequest request, EshopDbContext db, CancellationToken cancellationToken)
    {
        var (category, errors) = await ValidateAsync(request, db, cancellationToken);
        if (category is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var product = new Product { Name = string.Empty, Description = string.Empty };
        Apply(request, category, product);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/back-office/products/{product.Id}", ToResponse(product));
    }

    private static async Task<Results<Ok<BackOfficeProductResponse>, NotFound, ValidationProblem>> EditProduct(
        int id, ProductRequest request, EshopDbContext db, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var (category, errors) = await ValidateAsync(request, db, cancellationToken);
        if (category is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        Apply(request, category, product);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(product));
    }

    private static async Task<Results<NoContent, NotFound>> SetRetired(
        int id, bool retired, EshopDbContext db, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        product.IsRetired = retired;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // The raw request body is the photo (Content-Type image/jpeg|png|webp|svg+xml, at most 10 MB). It goes to the
    // image service's public area; the returned ID is then set on a Product.
    internal static async Task<Results<Created<ProductPhotoResponse>, StatusCodeHttpResult>> UploadPhoto(
        HttpRequest request, IImageStore images, CancellationToken cancellationToken)
    {
        var contentType = request.ContentType?.Split(';')[0].Trim();
        if (contentType is null || !ImageTypes.Allowed.Contains(contentType))
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (request.ContentLength > ImageTypes.MaxBytes)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > ImageTypes.MaxBytes)
        {
            return TypedResults.StatusCode(buffer.Length == 0 ? StatusCodes.Status400BadRequest : StatusCodes.Status413PayloadTooLarge);
        }

        buffer.Position = 0;
        var imageId = await images.SaveAsync(ImageArea.Public, buffer, contentType.ToLowerInvariant(), cancellationToken);
        var url = ImageTypes.PublicUrl(imageId);
        return TypedResults.Created(url, new ProductPhotoResponse(imageId, url));
    }

    private static async Task<(ProductCategory? Category, Dictionary<string, string[]> Errors)> ValidateAsync(
        ProductRequest request, EshopDbContext db, CancellationToken cancellationToken)
    {
        var errors = BackOfficeEndpoints.Errors();
        errors.Require(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 200, "name", "กรุณาระบุชื่อสินค้า (ไม่เกิน 200 ตัวอักษร)");
        errors.Require((request.Description?.Length ?? 0) <= 2000, "description", "รายละเอียดยาวเกิน 2,000 ตัวอักษร");
        errors.Require(request.Price is > 0m and < 10_000_000m && decimal.Round(request.Price, 2) == request.Price, "price", "ราคาต้องมากกว่า 0 บาท");
        errors.Require(request.ImageId is null || ImageId().IsMatch(request.ImageId), "imageId", "รหัสรูปภาพไม่ถูกต้อง");

        var category = await db.ProductCategories.SingleOrDefaultAsync(c => c.Code == request.ProductCategoryCode, cancellationToken);
        errors.Require(category is not null, "productCategoryCode", "ไม่พบประเภทสินค้านี้");

        return (errors.Count == 0 ? category : null, errors);
    }

    private static void Apply(ProductRequest request, ProductCategory category, Product product)
    {
        product.ProductCategoryId = category.Id;
        product.ProductCategory = category;
        product.Name = request.Name!.Trim();
        product.Description = request.Description?.Trim() ?? string.Empty;
        product.Price = request.Price;
        product.ImageId = request.ImageId;
    }

    private static BackOfficeProductResponse ToResponse(Product p) =>
        new(p.Id, p.Name, p.Description, p.Price, p.ProductCategory.Code, p.ImageId,
            p.ImageId is null ? null : ImageTypes.PublicUrl(p.ImageId), p.IsRetired);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex ImageId();
}

// ImageId: ID from POST /back-office/product-photos (or an existing photo's ID); null for no photo.
internal sealed record ProductRequest(string? ProductCategoryCode, string? Name, string? Description, decimal Price, string? ImageId);

internal sealed record BackOfficeProductResponse(
    int Id, string Name, string Description, decimal Price, string ProductCategoryCode, string? ImageId, string? ImageUrl, bool IsRetired);

internal sealed record ProductPhotoResponse(string ImageId, string ImageUrl);
