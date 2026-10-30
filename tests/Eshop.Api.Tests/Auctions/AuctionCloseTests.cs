using System.Net;

namespace Eshop.Api.Tests.Auctions;

/// <summary>
/// The end of an Auction: the highest Bid wins, the winner pays cash at a Branch within 3 days, otherwise Staff mark
/// the Lot Forfeited and may offer it again in a later Auction. Every test closes an Auction by moving the clock.
/// </summary>
[Collection(nameof(ClockMoving))]
public sealed class AuctionCloseTests(EshopApiFactory api)
{
    [Fact]
    public async Task At_close_the_highest_Bid_wins_and_the_winner_must_pay_cash_within_3_days()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 2);
        var (sold, unsold) = (lots[0], lots[1]);
        var winner = await api.IdVerifiedCustomerAsync();
        var runnerUp = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await runnerUp.BidAsync(sold.Id, 10_000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(sold.Id, 10_500m)).StatusCode);

        api.Clock.SetUtcNow(auction.ClosesAt);

        var won = await winner.GetLotAsync(sold.Id);
        Assert.Equal("awaiting-payment", won.LotStatus);
        Assert.True(won.YouWon);
        Assert.Equal(10_500m, won.HighestBid);
        Assert.Equal(auction.ClosesAt.AddDays(3), won.PaymentDueAt);
        Assert.True(won.CashOnly);
        Assert.False((await runnerUp.GetLotAsync(sold.Id)).YouWon);
        Assert.Null((await api.CreateClient().GetLotAsync(sold.Id)).YouWon);

        Assert.Equal("unsold", (await api.CreateClient().GetLotAsync(unsold.Id)).LotStatus);

        var wonLots = await winner.GetWonLotsAsync();
        var listed = Assert.Single(wonLots);
        Assert.Equal(sold.Id, listed.Id);
        Assert.Equal("awaiting-payment", listed.LotStatus);
        Assert.Empty(await runnerUp.GetWonLotsAsync());
    }

    [Fact]
    public async Task Staff_record_the_winners_cash_payment_and_Handover_at_a_Branch()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 1);
        var lot = lots[0];
        var winner = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(lot.Id, 12_000m)).StatusCode);
        var staff = await api.CreateStaffClientAsync();
        await (await staff.RecordPaymentAsync(lot.Id, "bkk-ladprao")).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-awaiting-payment");

        api.Clock.SetUtcNow(auction.ClosesAt.AddDays(2));
        staff = await api.CreateStaffClientAsync(); // Staff tokens last 12 hours.
        var awaiting = await staff.GetBackOfficeLotAsync(auction.Id, lot.Id);
        Assert.Equal("awaiting-payment", awaiting.LotStatus);
        Assert.Equal("สมหญิง ประมูลเก่ง", awaiting.Sale!.WinnerFullName);
        Assert.Equal(12_000m, awaiting.Sale.Amount);
        Assert.False(awaiting.Sale.MayForfeit);

        var response = await staff.RecordPaymentAsync(lot.Id, "bkk-ladprao");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sold = await staff.GetBackOfficeLotAsync(auction.Id, lot.Id);
        Assert.Equal("sold", sold.LotStatus);
        Assert.Equal(api.Clock.GetUtcNow(), sold.Sale!.PaidAt);
        Assert.Equal("bkk-ladprao", sold.Sale.PaymentBranchCode);
        Assert.Equal(EshopApiFactory.StaffUsername, sold.Sale.PaymentRecordedBy);
        Assert.Equal("sold", (await winner.GetLotAsync(lot.Id)).LotStatus);
        await (await staff.RecordPaymentAsync(lot.Id, "bkk-ladprao")).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-awaiting-payment");
        await (await staff.ForfeitAsync(lot.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-awaiting-payment");
    }

    [Fact]
    public async Task Payment_needs_a_known_Branch_and_a_winner()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 2);
        var winner = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(lots[0].Id, 10_000m)).StatusCode);
        api.Clock.SetUtcNow(auction.ClosesAt);
        var staff = await api.CreateStaffClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.RecordPaymentAsync(lots[0].Id, "no-such-branch")).StatusCode);
        await (await staff.RecordPaymentAsync(lots[1].Id, "bkk-ladprao")).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-awaiting-payment");
        Assert.Equal(HttpStatusCode.NotFound, (await staff.RecordPaymentAsync(int.MaxValue, "bkk-ladprao")).StatusCode);
    }

    [Fact]
    public async Task After_3_days_unpaid_Staff_mark_the_Lot_Forfeited_and_it_does_not_pass_to_the_second_highest_bidder()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 1);
        var lot = lots[0];
        var winner = await api.IdVerifiedCustomerAsync();
        var runnerUp = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await runnerUp.BidAsync(lot.Id, 10_000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(lot.Id, 10_100m)).StatusCode);

        api.Clock.SetUtcNow(auction.ClosesAt.AddDays(3).AddSeconds(-1));
        var staff = await api.CreateStaffClientAsync();
        var early = await (await staff.ForfeitAsync(lot.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "payment-window-open");
        Assert.Equal(auction.ClosesAt.AddDays(3), early.PaymentDueAt);

        api.Clock.SetUtcNow(auction.ClosesAt.AddDays(3));
        Assert.True((await staff.GetBackOfficeLotAsync(auction.Id, lot.Id)).Sale!.MayForfeit);
        var response = await staff.ForfeitAsync(lot.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forfeited = await staff.GetBackOfficeLotAsync(auction.Id, lot.Id);
        Assert.Equal("forfeited", forfeited.LotStatus);
        Assert.Equal(api.Clock.GetUtcNow(), forfeited.Sale!.ForfeitedAt);
        Assert.Equal(10_100m, forfeited.Sale.Amount);
        var seenByRunnerUp = await runnerUp.GetLotAsync(lot.Id);
        Assert.Equal("forfeited", seenByRunnerUp.LotStatus);
        Assert.False(seenByRunnerUp.YouWon);
        Assert.Empty(await runnerUp.GetWonLotsAsync());
        Assert.Equal("forfeited", Assert.Single(await winner.GetWonLotsAsync()).LotStatus);
        await (await staff.RecordPaymentAsync(lot.Id, "bkk-ladprao")).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-awaiting-payment");
        await (await staff.ForfeitAsync(lot.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-awaiting-payment");
    }

    [Fact]
    public async Task Staff_offer_a_Forfeited_or_unsold_Lot_again_in_a_later_Auction()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 3);
        var (forfeited, unsold, sold) = (lots[0], lots[1], lots[2]);
        var winner = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(forfeited.Id, 10_000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(sold.Id, 10_000m)).StatusCode);
        api.Clock.SetUtcNow(auction.ClosesAt.AddDays(3));
        var staff = await api.CreateStaffClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.RecordPaymentAsync(sold.Id, "bkk-bangna")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.ForfeitAsync(forfeited.Id)).StatusCode);
        var later = await staff.CreateAuctionAsync(new { });

        var again = await staff.ReofferAsync(forfeited.Id, later.Id, startingPrice: 8_000m);
        var unsoldAgain = await staff.ReofferAsync(unsold.Id, later.Id);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unsoldAgain.StatusCode);
        var listed = (await api.CreateClient().GetAuctionAsync(later.Id)).Lots;
        Assert.Equal(2, listed.Count);
        var reoffered = listed[0];
        Assert.Equal(forfeited.Title, reoffered.Title);
        Assert.Equal(forfeited.PhotoUrls, reoffered.PhotoUrls);
        Assert.Equal(8_000m, reoffered.StartingPrice);
        Assert.Null(reoffered.HighestBid);
        Assert.Equal("in-auction", reoffered.LotStatus);
        Assert.Equal(10_000m, listed[1].StartingPrice);
        Assert.Equal(reoffered.Id, (await api.CreateClient().GetLotAsync(forfeited.Id)).ReofferedAsLotId);

        await (await staff.ReofferAsync(forfeited.Id, later.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-reofferable");
        await (await staff.ReofferAsync(sold.Id, later.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-reofferable");
        await (await staff.ReofferAsync(lots[1].Id, auction.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-reofferable");
    }

    [Fact]
    public async Task A_Lot_still_awaiting_payment_or_in_an_open_Auction_cannot_be_offered_again()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 2);
        var winner = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(lots[0].Id, 10_000m)).StatusCode);
        var staff = await api.CreateStaffClientAsync();
        var later = await staff.CreateAuctionAsync(new { });
        await (await staff.ReofferAsync(lots[1].Id, later.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-reofferable");

        api.Clock.SetUtcNow(auction.ClosesAt);

        await (await staff.ReofferAsync(lots[0].Id, later.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-not-reofferable");
        await (await staff.ReofferAsync(lots[1].Id, auction.Id)).ExpectRefusalAsync(HttpStatusCode.Conflict, "auction-closed");
    }

    [Fact]
    public async Task A_won_Lot_is_cash_only_with_no_hire_purchase_path()
    {
        var (auction, lots) = await api.ShortAuctionAsync(lotCount: 1);
        var winner = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await winner.BidAsync(lots[0].Id, 10_000m)).StatusCode);
        api.Clock.SetUtcNow(auction.ClosesAt);

        var won = await winner.GetLotAsync(lots[0].Id);

        Assert.True(won.CashOnly);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.NotFound, (await winner.GetAsync($"/api/lots/{won.Id}/installment-plans?downPayment=0", ct)).StatusCode);
    }

    [Fact]
    public async Task Before_the_close_a_Lot_has_no_winner_yet()
    {
        var (_, lots) = await api.ShortAuctionAsync(lotCount: 1);
        var bidder = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await bidder.BidAsync(lots[0].Id, 10_000m)).StatusCode);

        var lot = await bidder.GetLotAsync(lots[0].Id);

        Assert.Equal("in-auction", lot.LotStatus);
        Assert.False(lot.YouWon);
        Assert.Null(lot.PaymentDueAt);
        Assert.Empty(await bidder.GetWonLotsAsync());
        var anonymous = await api.CreateClient().GetAsync("/api/lots/won", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
