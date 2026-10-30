using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.Auctions;

public sealed class AuctionAdministrationTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_Auction_defaults_to_the_next_weekly_schedule_Monday_10_00_to_Sunday_20_00_Bangkok()
    {
        var staff = await api.CreateStaffClientAsync();
        var now = api.Clock.GetUtcNow();

        var auction = await staff.CreateAuctionAsync(new { });

        var opens = auction.OpensAt.ToOffset(Bangkok.Offset);
        var closes = auction.ClosesAt.ToOffset(Bangkok.Offset);
        Assert.Equal(DayOfWeek.Monday, opens.DayOfWeek);
        Assert.Equal(new TimeSpan(10, 0, 0), opens.TimeOfDay);
        Assert.Equal(DayOfWeek.Sunday, closes.DayOfWeek);
        Assert.Equal(new TimeSpan(20, 0, 0), closes.TimeOfDay);
        Assert.Equal(TimeSpan.FromDays(6) + TimeSpan.FromHours(10), auction.ClosesAt - auction.OpensAt);
        Assert.True(auction.OpensAt > now && auction.OpensAt <= now.AddDays(7), $"{auction.OpensAt} should be the next Monday after {now}");
        Assert.Equal("upcoming", auction.Status);
    }

    [Fact]
    public async Task Staff_may_choose_other_open_and_close_times()
    {
        var staff = await api.CreateStaffClientAsync();
        var opensAt = api.Clock.GetUtcNow().AddHours(-1);
        var closesAt = api.Clock.GetUtcNow().AddDays(2);

        var auction = await staff.CreateAuctionAsync(new { opensAt, closesAt });

        Assert.Equal(opensAt, auction.OpensAt);
        Assert.Equal(closesAt, auction.ClosesAt);
        Assert.Equal("open", auction.Status);
    }

    [Fact]
    public async Task An_Auction_must_close_after_it_opens_and_in_the_future()
    {
        var staff = await api.CreateStaffClientAsync();
        var now = api.Clock.GetUtcNow();

        var backwards = await staff.PostAsJsonAsync(
            "/api/back-office/auctions", new { opensAt = now.AddDays(2), closesAt = now.AddDays(1) }, Ct);
        var alreadyClosed = await staff.PostAsJsonAsync(
            "/api/back-office/auctions", new { opensAt = now.AddDays(-3), closesAt = now.AddDays(-1) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, alreadyClosed.StatusCode);
    }
}

public sealed class LotTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staff_add_a_Lot_with_photos_and_visitors_see_it_with_no_Bids_yet()
    {
        var staff = await api.CreateStaffClientAsync();
        var auction = await staff.CreateAuctionAsync(new { });
        var front = TestData.NewPhoto();
        var frontId = await staff.UploadLotPhotoAsync(front);
        var backId = await staff.UploadLotPhotoAsync(TestData.NewPhoto());

        var added = await staff.AddLotAsync(auction.Id, new
        {
            title = "iPhone 15 Pro 256GB Natural Titanium",
            conditionNotes = "แบตเตอรี่ 88% มีรอยขนแมวเล็กน้อย ครบกล่อง",
            startingPrice = 18_000m,
            imageIds = new[] { frontId, backId },
        });

        var lot = await api.CreateClient().GetLotAsync(added.Id);
        Assert.Equal("iPhone 15 Pro 256GB Natural Titanium", lot.Title);
        Assert.Equal("แบตเตอรี่ 88% มีรอยขนแมวเล็กน้อย ครบกล่อง", lot.ConditionNotes);
        Assert.Equal(18_000m, lot.StartingPrice);
        Assert.Equal([$"/images/public/{frontId}", $"/images/public/{backId}"], lot.PhotoUrls);
        Assert.Null(lot.HighestBid);
        Assert.Equal(0, lot.BidCount);
        Assert.Equal(18_000m, lot.MinimumBid);
        Assert.Equal(front, api.Images.PublicImage(frontId));
        Assert.Contains(added.Id, (await api.CreateClient().GetAuctionAsync(auction.Id)).Lots.Select(l => l.Id));
    }

    [Fact]
    public async Task A_Lot_needs_a_title_a_photo_and_a_whole_baht_starting_price()
    {
        var staff = await api.CreateStaffClientAsync();
        var auction = await staff.CreateAuctionAsync(new { });

        var response = await staff.PostAsJsonAsync(
            $"/api/back-office/auctions/{auction.Id}/lots",
            new { title = " ", conditionNotes = "", startingPrice = 99.5m, imageIds = Array.Empty<string>() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemView>(Ct);
        Assert.Equal(["imageIds", "startingPrice", "title"], problem!.Errors.Keys.Order());
    }

    [Fact]
    public async Task Staff_can_correct_a_Lot_until_its_first_Bid()
    {
        var staff = await api.CreateStaffClientAsync();
        var lot = await api.OpenLotAsync(startingPrice: 10_000m);
        var photoId = await staff.UploadLotPhotoAsync(TestData.NewPhoto());
        var corrected = new { title = "iPhone 14 Plus 128GB Midnight", conditionNotes = "ครบกล่อง", startingPrice = 11_000m, imageIds = new[] { photoId } };

        var beforeBids = await staff.PutAsJsonAsync($"/api/back-office/lots/{lot.Id}", corrected, Ct);
        Assert.Equal(HttpStatusCode.OK, beforeBids.StatusCode);
        Assert.Equal(11_000m, (await api.CreateClient().GetLotAsync(lot.Id)).MinimumBid);

        var bidder = await api.IdVerifiedCustomerAsync();
        Assert.Equal(HttpStatusCode.Created, (await bidder.BidAsync(lot.Id, 11_000m)).StatusCode);
        var afterBids = await staff.PutAsJsonAsync($"/api/back-office/lots/{lot.Id}", corrected, Ct);

        await afterBids.ExpectRefusalAsync(HttpStatusCode.Conflict, "lot-has-bids");
        Assert.Equal("iPhone 14 Plus 128GB Midnight", (await api.CreateClient().GetLotAsync(lot.Id)).Title);
    }

    [Fact]
    public async Task An_unknown_Lot_is_not_found()
    {
        var response = await api.CreateClient().GetAsync("/api/lots/999999999", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ValidationProblemView(Dictionary<string, string[]> Errors);
}
