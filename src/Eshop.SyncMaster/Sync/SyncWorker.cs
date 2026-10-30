namespace Eshop.SyncMaster.Sync;

/// <summary>
/// Runs a <see cref="SyncCycle"/> right away and then every <c>Sync:Interval</c> (default 30 s). A failed
/// cycle is logged and retried on the next tick; it never stops the worker. Off when <c>Sync:Enabled</c> is
/// false (Seam-2 tests trigger cycles themselves).
/// </summary>
internal sealed class SyncWorker(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Sync:Enabled", true))
        {
            logger.LogInformation("Sync is disabled (Sync:Enabled=false)");
            return;
        }

        var interval = configuration.GetValue("Sync:Interval", TimeSpan.FromSeconds(30));
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SyncCycle>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync cycle failed; retrying on the next tick");
        }
    }
}
