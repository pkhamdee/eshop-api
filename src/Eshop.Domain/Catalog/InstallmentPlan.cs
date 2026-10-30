namespace Eshop.Domain.Catalog;

/// <summary>
/// One hire-purchase plan for a Product: a Term with its Installment (GLOSSARY: Term, Installment, Down Payment).
/// TotalPayable is everything the Customer pays: the Down Payment plus every Installment.
/// </summary>
public sealed record InstallmentPlan(decimal Price, decimal DownPayment, int Term, decimal Installment, decimal TotalPayable)
{
    /// <summary>The Terms eShop offers, before the Product Category's cap and the ฿500 floor.</summary>
    public static readonly IReadOnlyList<int> Terms = [6, 10, 12, 18, 24];

    /// <summary>No Installment may be smaller than this, in baht.</summary>
    public const decimal MinimumInstallment = 500m;

    /// <summary>A Down Payment can't be negative and must leave something to finance.</summary>
    public static bool IsValidDownPayment(decimal price, decimal downPayment) => downPayment >= 0 && downPayment < price;

    /// <summary>
    /// Every plan offered for a Product at this Down Payment, shortest Term first. Empty when the Down Payment is
    /// invalid or every Installment would fall under ฿500.
    /// </summary>
    public static IReadOnlyList<InstallmentPlan> Offered(decimal price, ProductCategory category, decimal downPayment) =>
        Terms.Select(term => For(price, category, term, downPayment)).OfType<InstallmentPlan>().ToList();

    /// <summary>The plan for one Term, or null when eShop doesn't offer it.</summary>
    public static InstallmentPlan? For(decimal price, ProductCategory category, int term, decimal downPayment)
    {
        if (!IsValidDownPayment(price, downPayment) || !Terms.Contains(term) || term > category.LongestTerm)
        {
            return null;
        }

        // Installment = ⌈(price − Down Payment) × (1 + Flat Rate × Term) ÷ Term⌉, so all Installments are equal.
        var installment = Math.Ceiling((price - downPayment) * (1 + (category.FlatRate * term)) / term);

        return installment < MinimumInstallment
            ? null
            : new InstallmentPlan(price, downPayment, term, installment, downPayment + (installment * term));
    }
}
