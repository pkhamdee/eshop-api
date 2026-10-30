using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Eshop.SyncMaster.Enterprise;

/// <summary>Readiness: sync-master can reach the Enterprise System's database.</summary>
internal sealed class EnterpriseHealthCheck(EnterpriseSystem enterprise) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await enterprise.CheckAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Enterprise database unreachable", ex);
        }
    }
}
