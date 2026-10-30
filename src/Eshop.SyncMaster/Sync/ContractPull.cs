using Eshop.Data;
using Eshop.Domain.Contracts;
using Eshop.SyncMaster.Enterprise;
using Microsoft.EntityFrameworkCore;

namespace Eshop.SyncMaster.Sync;

/// <summary>
/// Pull step of a sync cycle (stories 34, 35, 41, 60): Contract status and cancellations, then Payments, that
/// changed in the Enterprise System since the last pull. Each Enterprise table has a watermark in
/// <c>sync_watermarks</c> (the highest rowversion copied), saved in the same transaction as the copied rows,
/// so a crash re-pulls a batch at worst and never skips one. Each batch holds a PostgreSQL advisory lock, so
/// two pullers (e.g. old and new pod during a rolling update) take turns instead of copying a row twice. Unchanged rows are never read again. eShop
/// computes no status itself (overdue included): it takes what the Enterprise System says (ADR-0001). A
/// Contract set to Cancelled stops its Handover by itself (ADR-0004).
/// </summary>
internal sealed class ContractPull(
    EshopDbContext db, EnterpriseSystem enterprise, TimeProvider clock, ILogger<ContractPull> logger)
{
    public const string ContractSource = "hp.contract";

    public const string PaymentSource = "hp.payment";

    /// <summary>Advisory lock key shared by every sync-master pulling into this database.</summary>
    private const long PullLock = 10_000_010;

    /// <summary>Rows read per round trip; a cycle keeps reading until it has caught up.</summary>
    private const int BatchSize = 500;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Contracts first: a Payment's Contract is then already current when the Payment lands.
        while (await PullContractsAsync(cancellationToken) == BatchSize)
        {
        }

        while (await PullPaymentsAsync(cancellationToken) == BatchSize)
        {
        }
    }

    private async Task<int> PullContractsAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PullLock})", cancellationToken);
        var watermark = await WatermarkAsync(ContractSource, cancellationToken);
        var changed = await enterprise.ContractsChangedAfterAsync(watermark.LastRowVersion, BatchSize, cancellationToken);
        if (changed.Count == 0)
        {
            return 0;
        }

        var numbers = changed.Select(c => c.Number).ToList();
        var contracts = await db.Contracts.Where(c => numbers.Contains(c.Number)).ToDictionaryAsync(c => c.Number, cancellationToken);
        foreach (var state in changed)
        {
            if (!contracts.TryGetValue(state.Number, out var contract))
            {
                // Not one of ours (e.g. originated elsewhere in the Enterprise System): nothing to copy.
                continue;
            }

            var status = StatusFrom(state.Status);
            if (contract.Status != status)
            {
                logger.LogInformation(
                    "Contract {ContractNumber} is {Status} in the Enterprise System (was {Previous})",
                    contract.Number, status, contract.Status);
            }

            contract.RefreshFromEnterprise(status, state.CancelledAt);
        }

        Advance(watermark, changed[^1].RowVersion);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed.Count;
    }

    private async Task<int> PullPaymentsAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PullLock})", cancellationToken);
        var watermark = await WatermarkAsync(PaymentSource, cancellationToken);
        var changed = await enterprise.PaymentsChangedAfterAsync(watermark.LastRowVersion, BatchSize, cancellationToken);
        if (changed.Count == 0)
        {
            return 0;
        }

        var numbers = changed.Select(p => p.ContractNumber).Distinct().ToList();
        var contractIds = await db.Contracts.Where(c => numbers.Contains(c.Number))
            .ToDictionaryAsync(c => c.Number, c => c.Id, cancellationToken);
        var ids = changed.Select(p => p.Id).ToList();
        var known = await db.Payments.Where(p => ids.Contains(p.EnterprisePaymentId))
            .ToDictionaryAsync(p => p.EnterprisePaymentId, cancellationToken);

        foreach (var row in changed)
        {
            if (!contractIds.TryGetValue(row.ContractNumber, out var contractId))
            {
                continue;
            }

            if (!known.TryGetValue(row.Id, out var payment))
            {
                payment = new Payment { EnterprisePaymentId = row.Id, Channel = row.Channel };
                db.Payments.Add(payment);
                known[row.Id] = payment;
                logger.LogInformation(
                    "Payment {PaymentId} of {Amount} on Contract {ContractNumber} pulled from the Enterprise System",
                    row.Id, row.Amount, row.ContractNumber);
            }

            payment.ContractId = contractId;
            payment.InstallmentNumber = row.InstallmentNumber;
            payment.Amount = row.Amount;
            payment.PaidAt = row.PaidAt;
            payment.Channel = row.Channel;
        }

        Advance(watermark, changed[^1].RowVersion);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed.Count;
    }

    private async Task<SyncWatermark> WatermarkAsync(string source, CancellationToken cancellationToken)
    {
        var watermark = await db.SyncWatermarks.SingleOrDefaultAsync(w => w.Source == source, cancellationToken);
        if (watermark is null)
        {
            watermark = new SyncWatermark { Source = source };
            db.SyncWatermarks.Add(watermark);
        }

        return watermark;
    }

    private void Advance(SyncWatermark watermark, long rowVersion)
    {
        watermark.LastRowVersion = rowVersion;
        watermark.UpdatedAt = clock.GetUtcNow();
    }

    private static ContractStatus StatusFrom(string status) =>
        status switch
        {
            "ACTIVE" => ContractStatus.Active,
            "OVERDUE" => ContractStatus.Overdue,
            "PAID_OFF" => ContractStatus.PaidOff,
            "CANCELLED" => ContractStatus.Cancelled,
            _ => throw new InvalidOperationException($"Unknown Enterprise Contract status '{status}'."),
        };
}
