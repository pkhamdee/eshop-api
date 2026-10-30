namespace Eshop.SyncMaster.Sync;

/// <summary>
/// One sync run between eShop (PostgreSQL) and the Enterprise System (SQL Server): first push the queued
/// Contracts, then pull Contract status and Payments changed since the last run. <see cref="SyncWorker"/>
/// runs it every 30 seconds; Seam-2 tests run it once. Resolve it from a fresh DI scope per run.
/// </summary>
public sealed class SyncCycle
{
    private readonly ContractPush _push;
    private readonly ContractPull _pull;

    internal SyncCycle(ContractPush push, ContractPull pull)
    {
        _push = push;
        _pull = pull;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _push.RunAsync(cancellationToken);
        // After the push, so a Contract pushed in this cycle can already come back with its Enterprise state.
        await _pull.RunAsync(cancellationToken);
    }
}
