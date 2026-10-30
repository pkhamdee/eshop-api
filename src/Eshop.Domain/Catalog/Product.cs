namespace Eshop.Domain.Catalog;

/// <summary>
/// An item offered for hire purchase, belonging to exactly one Product Category (GLOSSARY: Product).
/// </summary>
public class Product
{
    public int Id { get; set; }

    public int ProductCategoryId { get; set; }

    public ProductCategory ProductCategory { get; set; } = null!;

    /// <summary>Display name (brand and model; Thai where appropriate).</summary>
    public required string Name { get; set; }

    /// <summary>Thai description shown on the Product page.</summary>
    public required string Description { get; set; }

    /// <summary>Current cash price in baht.</summary>
    public decimal Price { get; set; }

    /// <summary>
    /// ID of the Product photo in the image service's public area (eshop-node-api), e.g. <c>demo-iphone-17.svg</c>.
    /// Only the ID is stored here; the photo is served at <c>/images/public/{ImageId}</c>.
    /// </summary>
    public string? ImageId { get; set; }

    /// <summary>Retired Products stay in the database (Applications may refer to them) but leave the catalog.</summary>
    public bool IsRetired { get; set; }
}
