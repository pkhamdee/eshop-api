using Eshop.Data;
using Eshop.SyncMaster.Enterprise;
using Microsoft.EntityFrameworkCore;

namespace Eshop.SyncMaster.Sync;

/// <summary>
/// Push step of a sync cycle (stories 58, 59): every Contract waiting in the outbox (<c>contract_outbox</c>,
/// <c>pushed_at</c> null), oldest first, goes to the Enterprise System and is then marked pushed. The
/// Enterprise insert is idempotent on the Contract number, so a crash between the two steps only means the
/// Contract is pushed again next cycle, never duplicated. After the mark, eShop no longer owns the Contract
/// (ADR-0001): nothing here or in eshop-api writes it again; changes come back by the pull (#10).
/// </summary>
internal sealed class ContractPush(
    EshopDbContext db, EnterpriseSystem enterprise, TimeProvider clock, ILogger<ContractPush> logger)
{
    /// <summary>Contracts handled per cycle; the rest wait for the next one.</summary>
    private const int BatchSize = 100;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var pending = await db.ContractOutbox
            .Where(e => e.PushedAt == null)
            .OrderBy(e => e.EnqueuedAt).ThenBy(e => e.Id)
            .Take(BatchSize)
            .Include(e => e.Contract).ThenInclude(c => c.Application)
            .Include(e => e.Contract).ThenInclude(c => c.Customer)
            .Include(e => e.Contract).ThenInclude(c => c.Product)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            var contract = entry.Contract;
            var inserted = await enterprise.PushContractAsync(
                new EnterpriseContract(
                    contract.Number,
                    contract.Application.IdNumber,
                    contract.Application.FullName ?? contract.Customer.FullName ?? string.Empty,
                    contract.Customer.MobileNumber,
                    contract.Product.Name,
                    contract.Price,
                    contract.DownPayment,
                    contract.FlatRate,
                    contract.Term,
                    contract.Installment,
                    contract.TotalPayable,
                    contract.AcceptedAt),
                cancellationToken);

            // Marked one by one, so a failure later in the batch keeps what already went through.
            entry.PushedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                inserted
                    ? "Pushed Contract {ContractNumber} to the Enterprise System"
                    : "Contract {ContractNumber} was already in the Enterprise System; marked pushed",
                contract.Number);
        }

        return pending.Count;
    }
}
