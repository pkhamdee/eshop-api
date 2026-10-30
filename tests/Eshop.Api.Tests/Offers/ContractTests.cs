using System.Net;
using System.Text.RegularExpressions;
using Eshop.Api.Tests.Applications;
using Eshop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eshop.Api.Tests.Offers;

// Accepting an Offer with an SMS code creates the Contract (SP-YYYYMM-NNNNNN) and, in the same transaction,
// its outbox entry for eshop-sync-master (ADR-0001). A Customer holds at most one active Contract.
public sealed partial class ContractTests(EshopApiFactory api)
{
    [Fact]
    public async Task Accepting_with_the_SMS_code_creates_a_numbered_Contract_with_its_terms_and_schedule()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var application = await customer.ApproveAsync(api, downPayment: 3_000m, term: 6);

        var acceptedAt = api.Clock.GetUtcNow();
        var contract = await customer.AcceptWithSmsCodeAsync(api, application.Offer!.Id, mobile);

        var bangkokMonth = Bangkok.LocalTime(acceptedAt).ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Matches($"^SP-{bangkokMonth}-[0-9]{{6}}$", contract.Number);
        Assert.Equal("active", contract.Status);
        Assert.Equal(
            (application.Product.Name, application.Price, 3_000m, 6, application.Installment, application.TotalPayable, "store-pickup"),
            (contract.Product.Name, contract.Price, contract.DownPayment, contract.Term, contract.Installment, contract.TotalPayable, contract.Handover));
        Assert.Equal([1, 2, 3, 4, 5, 6], contract.Installments.Select(i => i.Number));
        Assert.All(contract.Installments, i => Assert.Equal((application.Installment, (DateOnly?)null), (i.Amount, i.DueDate))); // due dates come with Handover

        var fetched = await customer.GetContractAsync(contract.Number);
        Assert.Equal(contract.Installments, fetched.Installments);
        Assert.Empty(fetched.Payments);
        Assert.Equal(contract with { Installments = fetched.Installments, Payments = fetched.Payments }, fetched);
        Assert.Equal(contract.Number, Assert.Single(await customer.GetContractsAsync()).Number);
        var offer = await customer.GetOfferAsync(application.Offer.Id);
        Assert.Equal(("accepted", contract.Number), (offer.Status, offer.ContractNumber));
        Assert.Equal("accepted", (await customer.GetApplicationAsync(application.Id)).Offer!.Status);
    }

    [Fact]
    public async Task The_accept_SMS_tells_the_Customer_what_the_code_is_for()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var application = await customer.ApproveAsync(api);

        Assert.Equal(HttpStatusCode.Accepted, (await customer.RequestAcceptanceCodeAsync(application.Offer!.Id)).StatusCode);

        var text = api.Sms.SentTo(mobile).Last().Text;
        Assert.Contains("ยอมรับข้อเสนอ", text);
        Assert.Contains("eShop", text);
        Assert.DoesNotContain("S Phone", text);
    }

    [Fact]
    public async Task Accepting_queues_the_Contract_for_the_Enterprise_System_in_the_same_transaction()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var application = await customer.ApproveAsync(api);

        var contract = await customer.AcceptWithSmsCodeAsync(api, application.Offer!.Id, mobile);

        // The outbox is read by eshop-sync-master (seam 2), so the test reads the row the way it will.
        var entry = Assert.Single(await OutboxAsync(contract.Number));
        Assert.Equal((contract.AcceptedAt, (DateTimeOffset?)null), (entry.EnqueuedAt, entry.PushedAt));
    }

    [Fact]
    public async Task Contract_numbers_run_on_without_repeating()
    {
        var numbers = new List<string>();
        foreach (var _ in Enumerable.Range(0, 3))
        {
            var mobile = TestData.NewMobileNumber();
            var customer = await api.CreateCustomerClientAsync(mobile);
            var application = await customer.ApproveAsync(api);
            numbers.Add((await customer.AcceptWithSmsCodeAsync(api, application.Offer!.Id, mobile)).Number);
        }

        Assert.Equal(3, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches(ContractNumberFormat(), n));
    }

    [Fact]
    public async Task A_wrong_or_missing_SMS_code_creates_no_Contract()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var offerId = (await customer.ApproveAsync(api)).Offer!.Id;

        using var withoutRequesting = await customer.AcceptAsync(offerId, "123456");
        Assert.Equal(HttpStatusCode.BadRequest, withoutRequesting.StatusCode);
        Assert.Equal("code-invalid", (await withoutRequesting.ProblemAsync()).Error);

        (await customer.RequestAcceptanceCodeAsync(offerId)).EnsureSuccessStatusCode();
        var code = api.Sms.LatestCodeFor(mobile);
        using var wrong = await customer.AcceptAsync(offerId, code == "000000" ? "111111" : "000000");
        Assert.Equal("code-invalid", (await wrong.ProblemAsync()).Error);

        Assert.Equal("open", (await customer.GetOfferAsync(offerId)).Status);
        Assert.Empty(await customer.GetContractsAsync());
    }

    [Fact]
    public async Task A_sign_in_code_does_not_accept_an_Offer()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var offerId = (await customer.ApproveAsync(api)).Offer!.Id;
        await api.CreateCustomerClientAsync(mobile); // a fresh sign-in code arrives on the same phone

        using var response = await customer.AcceptAsync(offerId, api.Sms.LatestCodeFor(mobile));

        Assert.Equal("code-invalid", (await response.ProblemAsync()).Error);
    }

    [Fact]
    public async Task An_accepted_Offer_cannot_be_accepted_again_or_declined()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var offerId = (await customer.ApproveAsync(api)).Offer!.Id;
        await customer.AcceptWithSmsCodeAsync(api, offerId, mobile);

        using var again = await customer.RequestAcceptanceCodeAsync(offerId);
        using var decline = await customer.DeclineAsync(offerId);

        Assert.Equal((HttpStatusCode.Conflict, "offer-accepted"), (again.StatusCode, (await again.ProblemAsync()).Error));
        Assert.Equal((HttpStatusCode.Conflict, "offer-accepted"), (decline.StatusCode, (await decline.ProblemAsync()).Error));
        Assert.Single(await customer.GetContractsAsync());
    }

    [Fact]
    public async Task A_Customer_with_an_active_Contract_cannot_apply_again_even_with_another_mobile_number()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var idNumber = TestData.NewIdNumber();
        var application = await customer.ApproveAsync(api, idNumber: idNumber);
        await customer.AcceptWithSmsCodeAsync(api, application.Offer!.Id, mobile);

        Assert.Equal(new EligibilityView(false, "active-contract", null), await customer.EligibilityAsync());
        var photo = TestData.NewPhoto();
        using var again = await customer.SubmitAsync(new ApplicationForm(application.Product.Id, TestData.NewIdNumber()) { Photo = photo });
        Assert.Equal((HttpStatusCode.Conflict, "active-contract"), (again.StatusCode, (await again.ProblemAsync()).Error));
        Assert.Equal((false, false), api.Images.WhereStored(photo)); // nothing stored: no Application exists

        // The same person (ID number) on another mobile number holds the same active Contract.
        var otherPhone = await api.CreateCustomerClientAsync();
        using var fromOtherPhone = await otherPhone.SubmitAsync(new ApplicationForm(application.Product.Id, idNumber));
        Assert.Equal("active-contract", (await fromOtherPhone.ProblemAsync()).Error);
    }

    [Fact]
    public async Task Accepting_twice_at_the_same_moment_creates_one_Contract()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var offerId = (await customer.ApproveAsync(api)).Offer!.Id;
        (await customer.RequestAcceptanceCodeAsync(offerId)).EnsureSuccessStatusCode();
        var code = api.Sms.LatestCodeFor(mobile);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => customer.AcceptAsync(offerId, code)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var contract = Assert.Single(await customer.GetContractsAsync());
        Assert.Single(await OutboxAsync(contract.Number));
    }

    [Fact]
    public async Task A_Customer_cannot_see_another_Customers_Contract()
    {
        var mobile = TestData.NewMobileNumber();
        var owner = await api.CreateCustomerClientAsync(mobile);
        var contract = await owner.AcceptWithSmsCodeAsync(api, (await owner.ApproveAsync(api)).Offer!.Id, mobile);
        var stranger = await api.CreateCustomerClientAsync();

        using var response = await stranger.GetAsync($"/api/contracts/{contract.Number}", Offering.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<List<Domain.Contracts.ContractOutboxEntry>> OutboxAsync(string contractNumber)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        return await db.ContractOutbox.AsNoTracking().Where(e => e.ContractNumber == contractNumber).ToListAsync(Offering.Ct);
    }

    [GeneratedRegex("^SP-[0-9]{6}-[0-9]{6}$")]
    private static partial Regex ContractNumberFormat();
}
