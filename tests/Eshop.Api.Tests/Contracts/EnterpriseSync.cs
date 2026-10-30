using Eshop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eshop.Api.Tests.Contracts;

/// <summary>
/// Test-only port standing in for eshop-sync-master's pull (#10). Contract status, cancellations and Payments
/// change only in the Enterprise System, and reach eshop-api only as rows sync-master writes into PostgreSQL
/// (<c>contracts.status</c>/<c>cancelled_at</c> and <c>payments</c>); by design (ADR-0001) eshop-api has no
/// endpoint for them, so there is no HTTP seam to drive. This port writes those rows in SQL exactly as the pull
/// leaves them, which is the boundary between the two processes. It deliberately does not go through the domain
/// model: there, refreshing a Contract from the Enterprise System is reachable only by sync-master. The pull
/// itself is covered by the Seam-2 tests in Eshop.SyncMaster.Tests.
/// </summary>
internal static class EnterpriseSync
{
    private static long _lastEnterprisePaymentId = 9_000_000_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The Contract's status as pulled: <c>Active|Overdue|PaidOff|Cancelled</c> (eShop's stored names).</summary>
    public static Task PullStatusAsync(this EshopApiFactory api, string contractNumber, string status, DateTimeOffset? cancelledAt = null) =>
        api.ExecuteAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE contracts SET status = {status}, cancelled_at = {cancelledAt} WHERE number = {contractNumber}", Ct));

    /// <summary>A Payment as pulled (<paramref name="channel"/> in Enterprise form: COUNTER|TRANSFER|PROMPTPAY).</summary>
    public static Task PullPaymentAsync(
        this EshopApiFactory api, string contractNumber, int? installmentNumber, decimal amount, DateTimeOffset paidAt, string channel)
    {
        var enterprisePaymentId = Interlocked.Increment(ref _lastEnterprisePaymentId);
        return api.ExecuteAsync(db => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO payments (enterprise_payment_id, contract_id, installment_number, amount, paid_at, channel)
            SELECT {enterprisePaymentId}, id, {installmentNumber}, {amount}, {paidAt}, {channel} FROM contracts WHERE number = {contractNumber}
            """, Ct));
    }

    private static async Task ExecuteAsync(this EshopApiFactory api, Func<EshopDbContext, Task<int>> write)
    {
        await using var scope = api.Services.CreateAsyncScope();
        Assert.Equal(1, await write(scope.ServiceProvider.GetRequiredService<EshopDbContext>()));
    }
}
