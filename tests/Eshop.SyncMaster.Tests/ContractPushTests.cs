namespace Eshop.SyncMaster.Tests;

/// <summary>Stories 58, 59, 61: accepted Contracts reach the Enterprise System exactly once, which then owns them.</summary>
public sealed class ContractPushTests(SyncMasterFactory sync)
{
    [Fact]
    public async Task A_queued_Contract_reaches_the_Enterprise_System_and_leaves_the_queue()
    {
        var contract = await sync.Eshop.QueueContractAsync("สมหญิง ส่งสัญญา");

        await sync.RunSyncCycleAsync();

        var row = Assert.Single(await sync.Enterprise.ContractsNumberedAsync(contract.Number));
        Assert.Equal(
            new EnterpriseContractRow(
                contract.Number, contract.IdNumber, "สมหญิง ส่งสัญญา", contract.MobileNumber, contract.ProductName,
                contract.Price, contract.DownPayment, contract.FlatRate, contract.Term, contract.Installment,
                contract.TotalPayable, "ACTIVE"),
            row);
        Assert.NotNull(await sync.Eshop.PushedAtAsync(contract.Number));
    }

    [Fact]
    public async Task A_Contract_pushed_twice_exists_only_once_in_the_Enterprise_System()
    {
        var contract = await sync.Eshop.QueueContractAsync();
        await sync.RunSyncCycleAsync();

        await sync.Eshop.RequeueAsync(contract.Number);
        await sync.RunSyncCycleAsync();

        Assert.Single(await sync.Enterprise.ContractsNumberedAsync(contract.Number));
        Assert.NotNull(await sync.Eshop.PushedAtAsync(contract.Number));
    }

    [Fact]
    public async Task Pushing_again_never_overwrites_what_the_Enterprise_System_changed()
    {
        var contract = await sync.Eshop.QueueContractAsync();
        await sync.RunSyncCycleAsync();
        await sync.Enterprise.CancelAsync(contract.Number);

        await sync.Eshop.RequeueAsync(contract.Number);
        await sync.RunSyncCycleAsync();

        var row = Assert.Single(await sync.Enterprise.ContractsNumberedAsync(contract.Number));
        Assert.Equal("CANCELLED", row.Status);
    }

    [Fact]
    public async Task Several_queued_Contracts_are_all_pushed_in_one_cycle()
    {
        var first = await sync.Eshop.QueueContractAsync();
        var second = await sync.Eshop.QueueContractAsync();

        await sync.RunSyncCycleAsync();

        Assert.Single(await sync.Enterprise.ContractsNumberedAsync(first.Number));
        Assert.Single(await sync.Enterprise.ContractsNumberedAsync(second.Number));
    }
}
