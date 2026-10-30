using Eshop.Api.Tests.Offers;

namespace Eshop.Api.Tests.Handovers;

// The first Installment falls due one month after the Handover date (Asia/Bangkok), then monthly; a due day
// that a shorter month lacks becomes that month's last day.
[Collection(nameof(ClockMoving))]
public sealed class InstallmentDueDateTests(EshopApiFactory api)
{
    [Fact]
    public async Task Due_dates_run_monthly_from_the_Bangkok_Handover_date()
    {
        var onJanuary31 = await api.AcceptedContractAsync();
        var justAfterMidnight = await api.AcceptedContractAsync();

        // The next 31 January in a year that isn't a leap year, so February has 28 days.
        var now = api.Clock.GetUtcNow();
        var year = Bangkok.LocalTime(now).Year;
        while (DateTime.IsLeapYear(year) || new DateTimeOffset(year, 1, 31, 10, 0, 0, Bangkok.Offset) <= now)
        {
            year++;
        }

        api.Clock.Advance(new DateTimeOffset(year, 1, 31, 10, 0, 0, Bangkok.Offset) - now);
        await HandOverAsync(onJanuary31.Number);
        api.Clock.Advance(TimeSpan.FromHours(14) + TimeSpan.FromMinutes(30)); // 1 February 00:30 in Bangkok, still 31 January UTC
        await HandOverAsync(justAfterMidnight.Number);

        Assert.Equal(
            [new(year, 2, 28), new(year, 3, 31), new(year, 4, 30), new(year, 5, 31), new(year, 6, 30), new(year, 7, 31)],
            await DueDatesAsync(onJanuary31));
        Assert.Equal(
            [new(year, 3, 1), new(year, 4, 1), new(year, 5, 1), new(year, 6, 1), new(year, 7, 1), new(year, 8, 1)],
            await DueDatesAsync(justAfterMidnight));
    }

    private async Task HandOverAsync(string contractNumber)
    {
        var staff = await api.CreateStaffClientAsync(); // a fresh sign-in: time has moved past the last token's expiry
        await staff.MoveThroughAsync(contractNumber, "ready-for-pickup", "handed-over");
    }

    private async Task<List<DateOnly>> DueDatesAsync(AcceptedContract accepted)
    {
        var customer = await api.CreateCustomerClientAsync(accepted.MobileNumber);
        var contract = await customer.GetContractAsync(accepted.Number);
        return [.. contract.Installments.Select(i => i.DueDate!.Value)];
    }
}
