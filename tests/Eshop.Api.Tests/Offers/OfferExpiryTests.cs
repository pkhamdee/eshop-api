using System.Net;
using Eshop.Api.Tests.Applications;

namespace Eshop.Api.Tests.Offers;

// An Offer not acted on expires 7 days after approval, on the controlled clock. Customer tokens last 7 days
// too, so the Customer signs in again after the clock moves.
[Collection(nameof(ClockMoving))]
public sealed class OfferExpiryTests(EshopApiFactory api)
{
    [Fact]
    public async Task An_Offer_is_open_for_7_days_after_approval_then_expires()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var application = await customer.ApproveAsync(api);
        var offerId = application.Offer!.Id;
        var approvedAt = application.DecidedAt!.Value;

        api.Clock.Advance(approvedAt + TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1) - api.Clock.GetUtcNow());
        customer = await api.CreateCustomerClientAsync(mobile);
        Assert.Equal("open", (await customer.GetOfferAsync(offerId)).Status);
        (await customer.RequestAcceptanceCodeAsync(offerId)).EnsureSuccessStatusCode();
        var code = api.Sms.LatestCodeFor(mobile);

        api.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("expired", (await customer.GetOfferAsync(offerId)).Status);
        using var accept = await customer.AcceptAsync(offerId, code);
        Assert.Equal((HttpStatusCode.Conflict, "offer-expired"), (accept.StatusCode, (await accept.ProblemAsync()).Error));
        using var decline = await customer.DeclineAsync(offerId);
        Assert.Equal("offer-expired", (await decline.ProblemAsync()).Error);
        Assert.Empty(await customer.GetContractsAsync());
        Assert.Equal("expired", (await customer.GetApplicationAsync(application.Id)).Offer!.Status);
    }

    [Fact]
    public async Task After_an_Offer_expires_the_Customer_may_apply_again()
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        await customer.ApproveAsync(api);

        api.Clock.Advance(TimeSpan.FromDays(7));
        customer = await api.CreateCustomerClientAsync(mobile);

        Assert.Equal(new EligibilityView(true, null, null), await customer.EligibilityAsync());
        Assert.Equal("open", (await customer.ApproveAsync(api)).Offer!.Status);
    }
}
