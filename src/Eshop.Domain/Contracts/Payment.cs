namespace Eshop.Domain.Contracts;

/// <summary>
/// Money received against a Contract's Installments (GLOSSARY: Payment), as recorded by the Enterprise System.
/// eShop never records one (ADR-0001): eshop-sync-master copies them here, keyed by the Enterprise System's
/// payment id so pulling the same Payment again only refreshes it.
/// </summary>
public class Payment
{
    public long Id { get; set; }

    /// <summary>The Enterprise System's id for this Payment (<c>hp.payment.payment_id</c>); unique.</summary>
    public long EnterprisePaymentId { get; set; }

    public int ContractId { get; set; }

    public Contract Contract { get; set; } = null!;

    /// <summary>The Installment it was taken for, when the Enterprise System says so.</summary>
    public int? InstallmentNumber { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    /// <summary>As the Enterprise System names it: <c>COUNTER | TRANSFER | PROMPTPAY</c>.</summary>
    public required string Channel { get; set; }
}
