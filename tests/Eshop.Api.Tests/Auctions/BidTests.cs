using System.Net;

namespace Eshop.Api.Tests.Auctions;

public sealed class BidTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_Bid_may_be_the_starting_price_and_becomes_the_current_highest_Bid()
    {
        var lot = await api.OpenLotAsync(startingPrice: 15_000m);
        var bidder = await api.IdVerifiedCustomerAsync();

        var response = await bidder.BidAsync(lot.Id, 15_000m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var seen = await api.CreateClient().GetLotAsync(lot.Id);
        Assert.Equal(15_000m, seen.HighestBid);
        Assert.Equal(1, seen.BidCount);
        Assert.Equal(15_100m, seen.MinimumBid);
        Assert.True((await bidder.GetLotAsync(lot.Id)).YouAreHighestBidder);
    }

    [Fact]
    public async Task A_first_Bid_below_the_starting_price_is_refused()
    {
        var lot = await api.OpenLotAsync(startingPrice: 15_000m);
        var bidder = await api.IdVerifiedCustomerAsync();

        var response = await bidder.BidAsync(lot.Id, 14_999m);

        var refusal = await response.ExpectRefusalAsync(HttpStatusCode.Conflict, "bid-too-low");
        Assert.Equal(15_000m, refusal.MinimumBid);
        Assert.Null((await api.CreateClient().GetLotAsync(lot.Id)).HighestBid);
    }

    [Fact]
    public async Task A_Bid_must_beat_the_current_highest_by_at_least_100_baht_so_equal_Bids_are_refused()
    {
        var lot = await api.OpenLotAsync(startingPrice: 10_000m);
        var first = await api.IdVerifiedCustomerAsync();
        var second = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await first.BidAsync(lot.Id, 12_000m)).StatusCode);

        var equal = await second.BidAsync(lot.Id, 12_000m);
        var only99More = await second.BidAsync(lot.Id, 12_099m);
        var lower = await second.BidAsync(lot.Id, 11_000m);
        var exactly100More = await second.BidAsync(lot.Id, 12_100m);

        var equalRefusal = await equal.ExpectRefusalAsync(HttpStatusCode.Conflict, "bid-too-low");
        await only99More.ExpectRefusalAsync(HttpStatusCode.Conflict, "bid-too-low");
        await lower.ExpectRefusalAsync(HttpStatusCode.Conflict, "bid-too-low");
        Assert.Equal(12_100m, equalRefusal.MinimumBid);
        Assert.Equal(HttpStatusCode.Created, exactly100More.StatusCode);
        var seen = await first.GetLotAsync(lot.Id);
        Assert.Equal(12_100m, seen.HighestBid);
        Assert.Equal(2, seen.BidCount);
        Assert.False(seen.YouAreHighestBidder);
    }

    [Fact]
    public async Task Bids_are_whole_baht()
    {
        var lot = await api.OpenLotAsync(startingPrice: 10_000m);
        var bidder = await api.IdVerifiedCustomerAsync();

        var response = await bidder.BidAsync(lot.Id, 10_000.50m);

        await response.ExpectRefusalAsync(HttpStatusCode.BadRequest, "bid-amount-invalid");
    }

    [Fact]
    public async Task A_Customer_who_has_not_passed_ID_Verification_is_told_to_verify_first()
    {
        var lot = await api.OpenLotAsync(startingPrice: 10_000m);
        var unverified = await api.CreateCustomerClientAsync();

        var response = await unverified.BidAsync(lot.Id, 10_000m);

        await response.ExpectRefusalAsync(HttpStatusCode.Forbidden, "id-verification-required");
        Assert.Null((await api.CreateClient().GetLotAsync(lot.Id)).HighestBid);
    }

    [Fact]
    public async Task A_Customer_under_20_may_not_bid_even_though_their_ID_is_verified()
    {
        var lot = await api.OpenLotAsync(startingPrice: 10_000m);
        var under20 = await api.IdVerifiedCustomerUnder20Async();

        var response = await under20.BidAsync(lot.Id, 10_000m);

        await response.ExpectRefusalAsync(HttpStatusCode.Forbidden, "under-20");
        Assert.Null((await api.CreateClient().GetLotAsync(lot.Id)).HighestBid);
    }

    [Fact]
    public async Task Visitors_and_Staff_cannot_bid()
    {
        var lot = await api.OpenLotAsync(startingPrice: 10_000m);
        var staff = await api.CreateStaffClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().BidAsync(lot.Id, 10_000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.BidAsync(lot.Id, 10_000m)).StatusCode);
    }

    [Fact]
    public async Task Bidding_on_an_unknown_Lot_is_not_found()
    {
        var bidder = await api.IdVerifiedCustomerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bidder.BidAsync(999_999_999, 10_000m)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_equal_Bids_on_one_Lot_never_tie_exactly_one_is_accepted()
    {
        var lot = await api.OpenLotAsync(startingPrice: 20_000m);
        var bidders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.IdVerifiedCustomerAsync()));

        for (var round = 0; round < 5; round++)
        {
            var amount = 20_000m + (round * 100m);
            using var start = new SemaphoreSlim(0);
            var bids = bidders.Select(async b =>
            {
                await start.WaitAsync(Ct);
                return await b.BidAsync(lot.Id, amount);
            }).ToList();
            start.Release(bidders.Length);
            var responses = await Task.WhenAll(bids);

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
                r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        }

        var seen = await api.CreateClient().GetLotAsync(lot.Id);
        Assert.Equal(20_400m, seen.HighestBid);
        Assert.Equal(5, seen.BidCount);
    }

    [Fact]
    public async Task Concurrent_Bids_less_than_100_baht_apart_never_both_win()
    {
        var lot = await api.OpenLotAsync(startingPrice: 30_000m);
        var bidders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.IdVerifiedCustomerAsync()));

        // 30,000 30,010 … 30,070: whichever lands first wins; every other is less than ฿100 above it.
        using var start = new SemaphoreSlim(0);
        var bids = bidders.Select(async (b, i) =>
        {
            await start.WaitAsync(Ct);
            return await b.BidAsync(lot.Id, 30_000m + (i * 10m));
        }).ToList();
        start.Release(bidders.Length);
        var responses = await Task.WhenAll(bids);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, (await api.CreateClient().GetLotAsync(lot.Id)).BidCount);
    }
}
