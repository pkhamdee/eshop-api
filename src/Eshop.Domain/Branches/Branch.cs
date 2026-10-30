namespace Eshop.Domain.Branches;

/// <summary>
/// An eShop Branch where a Customer can collect goods and pay at Handover (GLOSSARY: Branch).
/// </summary>
public class Branch
{
    public int Id { get; set; }

    /// <summary>Stable identifier, e.g. <c>bkk-ladprao</c>.</summary>
    public required string Code { get; set; }

    /// <summary>Thai display name.</summary>
    public required string Name { get; set; }

    public required string Address { get; set; }

    /// <summary>Thai province name.</summary>
    public required string Province { get; set; }

    /// <summary>Retired Branches stay in the database (Handovers may refer to them) but are no longer offered.</summary>
    public bool IsRetired { get; set; }
}
