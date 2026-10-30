namespace Eshop.Api.Tests.Applications;

// Time-dependent Application rules, driven by the controlled clock: the 60-second Credit Decision deadline
// and the 30-day wait after a Rejection.
[Collection(nameof(ClockMoving))]
public sealed class ApplicationTimingTests(EshopApiFactory api)
{
    private static readonly DateOnly Adult = new(1985, 2, 20);

    [Fact]
    public async Task No_decision_within_60_seconds_is_Rejected_as_timed_out_and_the_Customer_may_retry_at_once()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "ประเสริฐ ช้ามาก", Adult);
        api.CreditCheck.NeverAnswers(idNumber);
        var iPhone = await customer.IPhone17Async();
        var submitted = await customer.SubmitAcceptedAsync(new ApplicationForm(iPhone, idNumber));

        api.Clock.Advance(TimeSpan.FromSeconds(59));
        await WaitUntilIdVerifiedAsync(customer);
        Assert.Equal("reviewing", (await customer.GetApplicationAsync(submitted.Id)).Status);

        api.Clock.Advance(TimeSpan.FromSeconds(1));
        var decided = await customer.WaitForDecisionAsync(submitted.Id);

        Assert.Equal(("rejected", "timed-out"), (decided.Status, decided.Reason));
        Assert.Null(decided.ReapplyFrom);
        Assert.Equal(new EligibilityView(true, null, null), await customer.EligibilityAsync());

        var retryId = TestData.NewIdNumber();
        api.IdVerification.Verifies(retryId, "ประเสริฐ ช้ามาก", Adult);
        api.CreditCheck.Passes(retryId, 50_000m);
        Assert.Equal("approved", (await customer.ApplyAndWaitAsync(new ApplicationForm(iPhone, retryId))).Status);
    }

    [Fact]
    public async Task A_hanging_ID_Verification_also_times_out()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.NeverAnswers(idNumber);
        var submitted = await customer.SubmitAcceptedAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        api.Clock.Advance(TimeSpan.FromSeconds(60));
        var decided = await customer.WaitForDecisionAsync(submitted.Id);

        Assert.Equal(("rejected", "timed-out"), (decided.Status, decided.Reason));
    }

    [Fact]
    public async Task A_Rejected_Customer_may_apply_again_30_days_after_the_Credit_Decision()
    {
        var mobileNumber = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobileNumber);
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "กมลา รอสามสิบวัน", Adult);
        api.CreditCheck.Fails(idNumber);
        var rejected = await customer.ApplyAndWaitAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        api.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));
        customer = await api.CreateCustomerClientAsync(mobileNumber); // the 7-day token has expired by now
        Assert.Equal(
            new EligibilityView(false, "recently-rejected", rejected.DecidedAt + TimeSpan.FromDays(30)),
            await customer.EligibilityAsync());

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new EligibilityView(true, null, null), await customer.EligibilityAsync());
    }

    private static async Task WaitUntilIdVerifiedAsync(HttpClient customer)
    {
        var giveUpAt = DateTime.UtcNow.AddSeconds(10);
        while (!(await customer.MeAsync()).IdVerified && DateTime.UtcNow < giveUpAt)
        {
            await Task.Delay(25, Applying.Ct);
        }
    }
}
