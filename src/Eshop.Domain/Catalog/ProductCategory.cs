namespace Eshop.Domain.Catalog;

/// <summary>
/// A grouping of Products that sets the Flat Rate and the longest Term allowed (GLOSSARY: Product Category).
/// </summary>
public class ProductCategory
{
    public int Id { get; set; }

    /// <summary>Stable, URL-safe identifier, e.g. <c>mobile-phone</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Thai display name.</summary>
    public required string Name { get; set; }

    /// <summary>Monthly Flat Rate as a fraction, e.g. 0.015 for 1.5% per month.</summary>
    public decimal FlatRate { get; set; }

    /// <summary>The longest Term (number of monthly Installments) offered in this category.</summary>
    public int LongestTerm { get; set; }

    /// <summary>Display order in the catalog.</summary>
    public int SortOrder { get; set; }

    public List<Product> Products { get; set; } = [];
}
