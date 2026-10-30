using System.Globalization;
using System.Linq.Expressions;
using Eshop.Domain.Applications;
using Eshop.Domain.Catalog;
using Eshop.Domain.Customers;
using Eshop.Domain.Offers;

namespace Eshop.Domain.Contracts;

/// <summary>Contract status as known by the Enterprise System (story 34). eShop only ever creates Active ones.</summary>
public enum ContractStatus
{
    Active,
    Overdue,
    PaidOff,
    Cancelled,
}

/// <summary>
/// The binding hire-purchase agreement created when a Customer accepts an Offer (GLOSSARY: Contract).
/// eShop owns it only until eshop-sync-master has pushed it to the Enterprise System (ADR-0001); after
/// that its status and Payments arrive by sync and eShop never edits it. A Customer holds at most one
/// active Contract.
/// </summary>
public class Contract
{
    public int Id { get; private set; }

    /// <summary><c>SP-YYYYMM-NNNNNN</c>; unique, and the key the Enterprise System push is idempotent on.</summary>
    public string Number { get; private set; } = null!;

    public int OfferId { get; private set; }

    public Offer Offer { get; private set; } = null!;

    public int ApplicationId { get; private set; }

    public Application Application { get; private set; } = null!;

    public int CustomerId { get; private set; }

    public Customer Customer { get; private set; } = null!;

    public int ProductId { get; private set; }

    public Product Product { get; private set; } = null!;

    /// <summary>Locked price (from the Application).</summary>
    public decimal Price { get; private set; }

    public decimal FlatRate { get; private set; }

    public decimal DownPayment { get; private set; }

    public int Term { get; private set; }

    public decimal Installment { get; private set; }

    public decimal TotalPayable { get; private set; }

    public DateTimeOffset AcceptedAt { get; private set; }

    /// <summary>
    /// As known by the Enterprise System: eShop sets Active on acceptance and never computes Overdue or
    /// anything else itself; later values arrive by sync (<see cref="RefreshFromEnterprise"/>).
    /// </summary>
    public ContractStatus Status { get; private set; }

    /// <summary>When the Enterprise System cancelled the Contract (null unless Cancelled).</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Payments the Enterprise System recorded, as last pulled.</summary>
    public List<Payment> Payments { get; } = [];

    /// <summary>
    /// Copies the Enterprise System's view of the Contract (ADR-0001). Internal: only eshop-sync-master's pull
    /// may call it (InternalsVisibleTo in Eshop.Domain.csproj), so eshop-api cannot change a pushed Contract.
    /// A Contract set to Cancelled here stops its Handover (ADR-0004).
    /// </summary>
    internal void RefreshFromEnterprise(ContractStatus status, DateTimeOffset? cancelledAt)
    {
        Status = status;
        CancelledAt = status == ContractStatus.Cancelled ? cancelledAt : null;
    }

    /// <summary>
    /// Active or Overdue: blocks a new Application (Eligibility) and a second accepted Offer. An expression so EF
    /// can translate it: <c>db.Contracts.Where(Contract.IsActive)</c>. (The partial unique index
    /// <c>ix_contracts_customer_id_active</c> states the same rule in SQL.)
    /// </summary>
    public static readonly Expression<Func<Contract, bool>> IsActive =
        c => c.Status == ContractStatus.Active || c.Status == ContractStatus.Overdue;

    internal static Contract From(Offer offer, ContractNumber number, DateTimeOffset now)
    {
        var application = offer.Application;
        return new()
        {
            Number = number.ToString(),
            Offer = offer,
            Application = application,
            CustomerId = offer.CustomerId,
            ProductId = application.ProductId,
            Product = application.Product,
            Price = application.Price,
            FlatRate = application.FlatRate,
            DownPayment = application.DownPayment,
            Term = application.Term,
            Installment = application.Installment,
            TotalPayable = application.TotalPayable,
            AcceptedAt = now,
            Status = ContractStatus.Active,
        };
    }

    /// <summary>
    /// The Installment schedule: <see cref="Term"/> equal Installments. Due dates are unknown until Handover;
    /// then Installment n falls due n months after <paramref name="handedOverOn"/> (the Handover date in
    /// Asia/Bangkok; a day missing from a shorter month becomes that month's last day).
    /// </summary>
    public IReadOnlyList<ScheduledInstallment> Schedule(DateOnly? handedOverOn) =>
        [.. Enumerable.Range(1, Term).Select(n => new ScheduledInstallment(n, Installment, handedOverOn?.AddMonths(n)))];
}

public sealed record ScheduledInstallment(int Number, decimal Amount, DateOnly? DueDate);

/// <summary>
/// <c>SP-YYYYMM-NNNNNN</c>: the month (Asia/Bangkok) the Offer was accepted and a running number that restarts
/// at 000001 each month. The running number comes from a per-month counter row that is incremented in the
/// same transaction that saves the Contract, so numbers have no gaps and never repeat.
/// </summary>
public readonly record struct ContractNumber(string Month, int Sequence)
{
    /// <summary>The counter key (<c>YYYYMM</c>, Asia/Bangkok) for a Contract accepted at <paramref name="acceptedAt"/>.</summary>
    public static string MonthOf(DateTimeOffset acceptedAt) =>
        Bangkok.LocalTime(acceptedAt).ToString("yyyyMM", CultureInfo.InvariantCulture);

    public override string ToString() => $"SP-{Month}-{Sequence.ToString("D6", CultureInfo.InvariantCulture)}";
}

/// <summary>Per-month running number for <see cref="ContractNumber"/>.</summary>
public class ContractNumberCounter
{
    /// <summary><c>YYYYMM</c> (Asia/Bangkok).</summary>
    public required string Month { get; set; }

    public int LastNumber { get; set; }
}

/// <summary>
/// Transactional outbox row (ADR-0001): written in the same transaction that creates the Contract, so every
/// Contract is queued for eshop-sync-master exactly once. Sync-master pushes rows with
/// <see cref="PushedAt"/> null to the Enterprise System, idempotently on <see cref="ContractNumber"/>,
/// then sets <see cref="PushedAt"/>.
/// </summary>
public class ContractOutboxEntry
{
    public long Id { get; set; }

    public int ContractId { get; set; }

    public Contract Contract { get; set; } = null!;

    public required string ContractNumber { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>Set by sync-master once the Enterprise System has the Contract; eShop stops owning it then.</summary>
    public DateTimeOffset? PushedAt { get; set; }

    public static ContractOutboxEntry For(Contract contract) =>
        new() { Contract = contract, ContractNumber = contract.Number, EnqueuedAt = contract.AcceptedAt };
}
