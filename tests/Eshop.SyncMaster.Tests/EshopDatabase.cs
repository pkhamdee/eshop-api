using System.Globalization;
using Eshop.Data;
using Eshop.Domain.Applications;
using Eshop.Domain.Catalog;
using Eshop.Domain.Contracts;
using Eshop.Domain.Customers;
using Eshop.Domain.Handovers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eshop.SyncMaster.Tests;

/// <summary>
/// eShop's PostgreSQL seen from sync-master's side: Contracts queued in the outbox the way eshop-api's
/// Offer acceptance queues them (Contract + outbox entry in one save), and the outbox state afterwards.
/// </summary>
public sealed class EshopDatabase(IServiceProvider services)
{
    private static int _lastSequence;

    /// <summary>
    /// Creates an accepted Contract for a new Customer and queues it for the Enterprise System. Numbers use
    /// month 209912 so they never meet real ones.
    /// </summary>
    public async Task<QueuedContract> QueueContractAsync(string fullName = "สมหญิง ทดสอบระบบ")
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        var now = DateTimeOffset.UtcNow;

        var customer = new Customer { MobileNumber = NewMobileNumber(), CreatedAt = now };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        var product = await db.Products.Include(p => p.ProductCategory).OrderBy(p => p.Id).FirstAsync(p => !p.IsRetired, ct);
        var plan = InstallmentPlan.For(product.Price, product.ProductCategory, 12, 1_000m)
            ?? throw new InvalidOperationException("12-month plan not offered for the demo Product.");
        var application = Application.Submit(
            customer, product, plan, HandoverChoice.Delivery("99/1 ถนนทดสอบ เขตบางรัก", "กรุงเทพมหานคร"),
            NewIdNumber(), "test-id-card.jpg", "image/jpeg", now);
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct); // submitted (Reviewing), as eshop-api saves it before deciding
        application.RecordIdVerification(true, fullName, new DateOnly(1990, 5, 17), now);
        application.RecordCreditCheck(true, 1_000_000m, now);

        var contract = application.Offer!.Accept(new ContractNumber("209912", Interlocked.Increment(ref _lastSequence)), now);
        db.Contracts.Add(contract);
        db.ContractOutbox.Add(ContractOutboxEntry.For(contract));
        db.Handovers.Add(Handover.For(contract));
        await db.SaveChangesAsync(ct);

        return new QueuedContract(
            contract.Number, application.IdNumber, fullName, customer.MobileNumber, product.Name, contract.Price,
            contract.DownPayment, contract.FlatRate, contract.Term, contract.Installment, contract.TotalPayable);
    }

    /// <summary>When sync-master marked the Contract's outbox entry pushed (null = still queued).</summary>
    public async Task<DateTimeOffset?> PushedAtAsync(string contractNumber)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        return await db.ContractOutbox.Where(e => e.ContractNumber == contractNumber)
            .Select(e => e.PushedAt).SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Puts a pushed Contract back in the queue, as when sync-master crashed after the Enterprise System
    /// stored it but before it marked the entry pushed: the next cycle pushes it a second time.
    /// </summary>
    public async Task RequeueAsync(string contractNumber)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        await db.ContractOutbox.Where(e => e.ContractNumber == contractNumber)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PushedAt, (DateTimeOffset?)null), TestContext.Current.CancellationToken);
    }

    /// <summary>eShop's copy of the Contract: status, Payments (by Installment number) and whether its Handover stopped.</summary>
    public async Task<ContractCopy> ContractAsync(string contractNumber)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        var contract = await db.Contracts.AsNoTracking().Include(c => c.Payments).SingleAsync(c => c.Number == contractNumber, ct);
        var handover = await db.Handovers.AsNoTracking().SingleAsync(h => h.ContractId == contract.Id, ct);
        handover.Contract = contract;
        return new ContractCopy(
            contract.Status.ToString(),
            [.. contract.Payments.OrderBy(p => p.InstallmentNumber).Select(p => new PaymentCopy(p.InstallmentNumber, p.Amount, p.Channel))],
            handover.IsStopped);
    }

    /// <summary>Changes eShop's copy behind sync-master's back (status Active, no Payments).</summary>
    public async Task TamperAsync(string contractNumber)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        await db.Payments.Where(p => p.Contract.Number == contractNumber).ExecuteDeleteAsync(ct);
        await db.Contracts.Where(c => c.Number == contractNumber)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContractStatus.Active), ct);
    }

    private static string NewMobileNumber() =>
        "08" + Random.Shared.NextInt64(0, 100_000_000).ToString("D8", CultureInfo.InvariantCulture);

    private static string NewIdNumber() =>
        "9" + Random.Shared.NextInt64(0, 1_000_000_000_000).ToString("D12", CultureInfo.InvariantCulture);
}

public sealed record ContractCopy(string Status, IReadOnlyList<PaymentCopy> Payments, bool HandoverStopped);

public sealed record PaymentCopy(int? InstallmentNumber, decimal Amount, string Channel);

public sealed record QueuedContract(
    string Number, string IdNumber, string FullName, string MobileNumber, string ProductName, decimal Price,
    decimal DownPayment, decimal FlatRate, int Term, decimal Installment, decimal TotalPayable);
