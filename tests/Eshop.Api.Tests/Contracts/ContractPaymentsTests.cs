using Eshop.Api.Tests.Handovers;
using Eshop.Api.Tests.Offers;

namespace Eshop.Api.Tests.Contracts;

// Stories 34, 35: the Customer sees their Contract's status and Payments as known by the Enterprise System.
// Both arrive through eshop-sync-master (#10), which writes eShop's copy; the tests play its part (EnterpriseSync).
public sealed class ContractPaymentsTests(EshopApiFactory api)
{
    [Fact]
    public async Task A_new_Contract_has_no_Payments_yet()
    {
        var accepted = await api.AcceptedContractAsync();

        var contract = await accepted.Customer.GetContractAsync(accepted.Contract.Number);

        Assert.Equal(("active", 0m), (contract.Status, contract.AmountPaid));
        Assert.Empty(contract.Payments);
        Assert.Null(contract.CancelledAt);
    }

    [Fact]
    public async Task Payments_pulled_from_the_Enterprise_System_show_on_the_Contract_oldest_first()
    {
        var accepted = await api.AcceptedContractAsync();
        var number = accepted.Contract.Number;
        var installment = accepted.Contract.Installment;
        var first = new DateTimeOffset(2026, 2, 5, 4, 0, 0, TimeSpan.Zero);
        var second = first.AddMonths(1);

        await api.PullStatusAsync(number, "Overdue");
        await api.PullPaymentAsync(number, 2, installment, second, "PROMPTPAY");
        await api.PullPaymentAsync(number, 1, installment, first, "COUNTER");

        var contract = await accepted.Customer.GetContractAsync(number);
        Assert.Equal("overdue", contract.Status);
        Assert.Equal(
            [new PaymentView(1, installment, first, "counter"), new PaymentView(2, installment, second, "promptpay")],
            contract.Payments);
        Assert.Equal(installment * 2, contract.AmountPaid);
    }

    [Fact]
    public async Task A_Contract_paid_off_or_cancelled_in_the_Enterprise_System_says_so()
    {
        var paidOff = await api.AcceptedContractAsync();
        await api.PullStatusAsync(paidOff.Contract.Number, "PaidOff");
        Assert.Equal("paid-off", (await paidOff.Customer.GetContractAsync(paidOff.Contract.Number)).Status);

        var cancelled = await api.AcceptedContractAsync();
        var cancelledAt = new DateTimeOffset(2026, 1, 6, 2, 30, 0, TimeSpan.Zero);
        await api.PullStatusAsync(cancelled.Contract.Number, "Cancelled", cancelledAt);
        var contract = await cancelled.Customer.GetContractAsync(cancelled.Contract.Number);
        Assert.Equal(("cancelled", cancelledAt, "stopped"), (contract.Status, contract.CancelledAt, contract.HandoverStage));
        var summary = Assert.Single(await cancelled.Customer.GetContractsAsync());
        Assert.Equal("cancelled", summary.Status);
    }
}
