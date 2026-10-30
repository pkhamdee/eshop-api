using System.Net;

namespace Eshop.Api.Tests.Auctions;

[Collection(nameof(ClockMoving))]
public sealed class AuctionScheduleTests(EshopApiFactory api)
{
    [Fact]
    public async Task Bids_are_taken_from_the_opening_time_until_but_not_at_the_closing_time()
    {
        var staff = await api.CreateStaffClientAsync();
        var auction = await staff.CreateAuctionAsync(new { });
        var lot = await staff.AddLotAsync(auction.Id, new
        {
            title = "iPhone 13 128GB Blue",
            conditionNotes = "จอมีรอยเล็กน้อย",
            startingPrice = 9_000m,
            imageIds = new[] { await staff.UploadLotPhotoAsync(TestData.NewPhoto()) },
        });
        var mobileNumber = TestData.NewMobileNumber();
        await api.IdVerifiedCustomerAsync(mobileNumber);

        api.Clock.SetUtcNow(auction.OpensAt.AddSeconds(-1));
        var bidder = await api.CreateCustomerClientAsync(mobileNumber);
        await (await bidder.BidAsync(lot.Id, 9_000m)).ExpectRefusalAsync(HttpStatusCode.Conflict, "auction-not-open");
        Assert.Equal("upcoming", (await bidder.GetLotAsync(lot.Id)).AuctionStatus);

        api.Clock.SetUtcNow(auction.OpensAt);
        Assert.Equal(HttpStatusCode.Created, (await bidder.BidAsync(lot.Id, 9_000m)).StatusCode);

        api.Clock.SetUtcNow(auction.ClosesAt.AddSeconds(-1));
        bidder = await api.CreateCustomerClientAsync(mobileNumber);
        Assert.Equal(HttpStatusCode.Created, (await bidder.BidAsync(lot.Id, 9_100m)).StatusCode);

        api.Clock.SetUtcNow(auction.ClosesAt);
        await (await bidder.BidAsync(lot.Id, 9_500m)).ExpectRefusalAsync(HttpStatusCode.Conflict, "auction-closed");
        var closed = await bidder.GetLotAsync(lot.Id);
        Assert.Equal("closed", closed.AuctionStatus);
        Assert.Equal(9_100m, closed.HighestBid);
        Assert.True(closed.YouAreHighestBidder);
    }
}

public sealed class CurrentAuctionTests(EshopApiFactory api)
{
    [Fact]
    public async Task Visitors_browse_the_open_Auction_with_each_Lots_current_highest_Bid()
    {
        var staff = await api.CreateStaffClientAsync();
        var now = api.Clock.GetUtcNow();
        var auction = await staff.CreateAuctionAsync(new { opensAt = now, closesAt = now.AddDays(1) });
        var photoId = await staff.UploadLotPhotoAsync(TestData.NewPhoto());
        var lot = await staff.AddLotAsync(auction.Id, new
        {
            title = "iPhone 12 64GB White",
            conditionNotes = "แบตเตอรี่ 80%",
            startingPrice = 6_000m,
            imageIds = new[] { photoId },
        });
        var bidder = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await bidder.BidAsync(lot.Id, 6_500m)).StatusCode);

        var current = await api.CreateClient().GetCurrentAuctionAsync();

        Assert.Equal(auction.Id, current.Id);
        Assert.Equal("open", current.Status);
        var listed = Assert.Single(current.Lots);
        Assert.Equal("iPhone 12 64GB White", listed.Title);
        Assert.Equal([$"/images/public/{photoId}"], listed.PhotoUrls);
        Assert.Equal(6_500m, listed.HighestBid);
        Assert.Equal(6_600m, listed.MinimumBid);
        Assert.Null(listed.YouAreHighestBidder);
    }
}
