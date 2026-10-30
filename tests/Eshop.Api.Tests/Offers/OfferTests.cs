using System.Net;
using Eshop.Api.Tests.Applications;

namespace Eshop.Api.Tests.Offers;

// Offer (GLOSSARY): the price, Down Payment, Term and Installment of an Approved Application, which the
// Customer accepts (with an SMS code) or declines; it expires after 7 days.
public sealed class OfferTests(EshopApiFactory api)
{
    [Fact]
    public async Task An_Approved_Application_comes_with_an_Offer_showing_the_locked_terms()
    {
        var customer = await api.CreateCustomerClientAsync();
        var application = await customer.ApproveAsync(api, downPayment: 5_000m, term: 10);

        var offer = await customer.GetOfferAsync(application.Offer!.Id);

        Assert.Equal("open", offer.Status);
        Assert.Equal(application.DecidedAt!.Value + TimeSpan.FromDays(7), offer.ExpiresAt);
        Assert.Equal(
            (application.Product.Name, application.Price, 5_000m, 10, application.Installment, application.TotalPayable),
            (offer.Product.Name, offer.Price, offer.DownPayment, offer.Term, offer.Installment, offer.TotalPayable));
        Assert.Null(offer.ContractNumber);
    }

    [Fact]
    public async Task Declining_ends_the_Offer_creates_no_Contract_and_lets_the_Customer_apply_again_at_once()
    {
        var customer = await api.CreateCustomerClientAsync();
        var application = await customer.ApproveAsync(api);
        var offerId = application.Offer!.Id;

        using var declined = await customer.DeclineAsync(offerId);

        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        Assert.Equal("declined", (await customer.GetOfferAsync(offerId)).Status);
        Assert.Empty(await customer.GetContractsAsync());
        Assert.Equal(new EligibilityView(true, null, null), await customer.EligibilityAsync()); // no 30-day wait

        using var accept = await customer.RequestAcceptanceCodeAsync(offerId);
        Assert.Equal((HttpStatusCode.Conflict, "offer-declined"), (accept.StatusCode, (await accept.ProblemAsync()).Error));
    }

    [Fact]
    public async Task An_open_Offer_must_be_accepted_or_declined_before_applying_again()
    {
        var customer = await api.CreateCustomerClientAsync();
        var application = await customer.ApproveAsync(api);

        var eligibility = await customer.EligibilityAsync();
        Assert.Equal((false, "offer-open"), (eligibility.Eligible, eligibility.Reason));
        var photo = TestData.NewPhoto();
        using var again = await customer.SubmitAsync(new ApplicationForm(application.Product.Id, TestData.NewIdNumber()) { Photo = photo });
        Assert.Equal((HttpStatusCode.Conflict, "offer-open"), (again.StatusCode, (await again.ProblemAsync()).Error));
        Assert.Equal((false, false), api.Images.WhereStored(photo)); // no Application exists
    }

    [Fact]
    public async Task A_Customer_cannot_see_another_Customers_Offer()
    {
        var owner = await api.CreateCustomerClientAsync();
        var application = await owner.ApproveAsync(api);
        var stranger = await api.CreateCustomerClientAsync();

        using var response = await stranger.GetAsync($"/api/offers/{application.Offer!.Id}", Offering.Ct);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
