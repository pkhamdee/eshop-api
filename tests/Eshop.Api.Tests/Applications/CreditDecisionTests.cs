using System.Net;

namespace Eshop.Api.Tests.Applications;

// Credit Decision (spec #1): Approved only if ID Verification succeeds, the Credit Check passes and the
// financed amount (price − Down Payment) is within the Credit Limit; otherwise Rejected. Decided in the
// background; the Customer checks back.
public sealed class CreditDecisionTests(EshopApiFactory api)
{
    private static readonly DateOnly Adult = new(1990, 4, 12);

    [Fact]
    public async Task An_Application_is_Approved_when_ID_Verification_and_Credit_Check_pass_within_the_Credit_Limit()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "สมชาย ใจดี", Adult);
        api.CreditCheck.Passes(idNumber, 50_000m);
        var iPhone = await customer.IPhone17Async();

        var submitted = await customer.SubmitAcceptedAsync(new ApplicationForm(iPhone, idNumber));
        Assert.Equal("reviewing", submitted.Status);

        var decided = await customer.WaitForDecisionAsync(submitted.Id);

        Assert.Equal("approved", decided.Status);
        Assert.Null(decided.Reason);
        Assert.Equal(50_000m, decided.CreditLimit);
        // iPhone 17 ฿29,900, Term 12 at 1.5%: 29,900 × 1.18 / 12 = 2,940.17 → 2,941; total 12 × 2,941.
        Assert.Equal((29_900m, 0m, 12, 2_941m, 35_292m),
            (decided.Price, decided.DownPayment, decided.Term, decided.Installment, decided.TotalPayable));
        Assert.Equal(("สมชาย ใจดี", Adult), (decided.FullName, decided.DateOfBirth));
        Assert.Equal(("store-pickup", "bkk-ladprao"), (decided.Handover, decided.Branch?.Code));
    }

    [Fact]
    public async Task An_Application_is_Rejected_when_ID_Verification_fails()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Fails(idNumber);

        var decided = await customer.ApplyAndWaitAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        Assert.Equal(("rejected", "id-verification-failed"), (decided.Status, decided.Reason));
        Assert.Equal(decided.DecidedAt + TimeSpan.FromDays(30), decided.ReapplyFrom);
        Assert.Null(decided.FullName);
        Assert.False(api.CreditCheck.WasAskedAbout(idNumber));
        Assert.False((await customer.MeAsync()).IdVerified);
    }

    [Fact]
    public async Task An_Application_is_Rejected_when_the_Credit_Check_fails()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "มาลี รักไทย", Adult);
        api.CreditCheck.Fails(idNumber);

        var decided = await customer.ApplyAndWaitAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        Assert.Equal(("rejected", "credit-check-failed"), (decided.Status, decided.Reason));
        Assert.Equal(decided.DecidedAt + TimeSpan.FromDays(30), decided.ReapplyFrom);
    }

    [Fact]
    public async Task An_Application_is_Rejected_when_the_financed_amount_is_over_the_Credit_Limit()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "วิชัย มั่นคง", Adult);
        api.CreditCheck.Passes(idNumber, 5_000m);

        // iPad ฿12,900 with ฿7,899 down: ฿5,001 financed, one baht over the limit.
        var decided = await customer.ApplyAndWaitAsync(
            new ApplicationForm(await customer.IPadA16Async(), idNumber) { Term = 6, DownPayment = 7_899m });

        Assert.Equal(("rejected", "over-credit-limit"), (decided.Status, decided.Reason));
        Assert.Equal(5_000m, decided.CreditLimit);
    }

    [Fact]
    public async Task A_financed_amount_exactly_at_the_Credit_Limit_is_Approved()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "วิชัย มั่นคง", Adult);
        api.CreditCheck.Passes(idNumber, 5_000m);

        // iPad ฿12,900 with ฿7,900 down: ฿5,000 financed. Term 6: 5,000 × 1.09 / 6 = 908.33 → 909.
        var decided = await customer.ApplyAndWaitAsync(
            new ApplicationForm(await customer.IPadA16Async(), idNumber) { Term = 6, DownPayment = 7_900m });

        Assert.Equal("approved", decided.Status);
        Assert.Equal((909m, 7_900m + (6 * 909m)), (decided.Installment, decided.TotalPayable));
    }

    [Fact]
    public async Task A_Customer_who_passed_ID_Verification_is_marked_ID_verified()
    {
        var customer = await api.CreateCustomerClientAsync();
        Assert.False((await customer.MeAsync()).IdVerified);
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "สุดา สุขสันต์", Adult);
        api.CreditCheck.Fails(idNumber); // ID-verified even though the Application is Rejected

        await customer.ApplyAndWaitAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        Assert.True((await customer.MeAsync()).IdVerified);
    }

    [Fact]
    public async Task The_ID_card_photo_is_stored_as_a_private_image_and_never_exposed_by_a_link()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "อนันต์ บุญมี", Adult);
        api.CreditCheck.Passes(idNumber, 50_000m);
        var photo = TestData.NewPhoto();

        var decided = await customer.ApplyAndWaitAsync(
            new ApplicationForm(await customer.IPhone17Async(), idNumber) { Photo = photo });

        Assert.Equal((Private: true, Public: false), api.Images.WhereStored(photo));
        Assert.Equal([photo], api.IdVerification.PhotosCheckedFor(idNumber));
        Assert.Null(decided.Other); // nothing beyond the documented fields, so no image URL or ID
    }

    [Fact]
    public async Task A_Customer_cannot_see_another_Customers_Application()
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "กมลา ทองดี", Adult);
        api.CreditCheck.Passes(idNumber, 50_000m);
        var submitted = await customer.SubmitAcceptedAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        var someoneElse = await api.CreateCustomerClientAsync();
        var response = await someoneElse.GetAsync($"/api/applications/{submitted.Id}", Applying.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Applying_needs_a_signed_in_Customer()
    {
        var visitor = api.CreateClient();

        var submit = await visitor.SubmitAsync(new ApplicationForm(await visitor.IPhone17Async(), TestData.NewIdNumber()));
        var eligibility = await visitor.GetAsync("/api/eligibility", Applying.Ct);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (submit.StatusCode, eligibility.StatusCode));
    }
}
