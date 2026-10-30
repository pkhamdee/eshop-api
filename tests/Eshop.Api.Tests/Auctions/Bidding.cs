using System.Net.Http.Json;
using Eshop.Api.Tests.Applications;

namespace Eshop.Api.Tests.Auctions;

/// <summary>
/// What the Auction screens do, through the HTTP API only: Staff create Auctions and add Lots; visitors read
/// Lots; Customers place Bids.
/// </summary>
internal static class Bidding
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<AuctionView> CreateAuctionAsync(this HttpClient staff, object request)
    {
        using var response = await staff.PostAsJsonAsync("/api/back-office/auctions", request, Ct);
        await response.ExpectAsync(System.Net.HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuctionView>(Ct))!;
    }

    public static async Task<string> UploadLotPhotoAsync(this HttpClient staff, byte[] photo)
    {
        using var content = new ByteArrayContent(photo);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        using var response = await staff.PostAsync("/api/back-office/lot-photos", content, Ct);
        await response.ExpectAsync(System.Net.HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PhotoView>(Ct))!.ImageId;
    }

    public static async Task<LotView> AddLotAsync(this HttpClient staff, int auctionId, object request)
    {
        using var response = await staff.PostAsJsonAsync($"/api/back-office/auctions/{auctionId}/lots", request, Ct);
        await response.ExpectAsync(System.Net.HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LotView>(Ct))!;
    }

    public static async Task<LotView> GetLotAsync(this HttpClient client, int lotId) =>
        (await client.GetFromJsonAsync<LotView>($"/api/lots/{lotId}", Ct))!;

    public static async Task<AuctionView> GetCurrentAuctionAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<AuctionView>("/api/auctions/current", Ct))!;

    public static async Task<AuctionView> GetAuctionAsync(this HttpClient client, int auctionId) =>
        (await client.GetFromJsonAsync<AuctionView>($"/api/auctions/{auctionId}", Ct))!;

    /// <summary>A Lot (one photo) in a new Auction that opened an hour ago and closes in a week.</summary>
    public static async Task<LotView> OpenLotAsync(this EshopApiFactory api, decimal startingPrice)
    {
        var staff = await api.CreateStaffClientAsync();
        var now = api.Clock.GetUtcNow();
        var auction = await staff.CreateAuctionAsync(new { opensAt = now.AddHours(-1), closesAt = now.AddDays(7) });
        return await staff.AddLotAsync(auction.Id, new
        {
            title = "iPhone 14 128GB Midnight",
            conditionNotes = "สภาพดี ครบกล่อง",
            startingPrice,
            imageIds = new[] { await staff.UploadLotPhotoAsync(TestData.NewPhoto()) },
        });
    }

    /// <summary>
    /// A new Auction that opened an hour ago and closes a minute from now, with <paramref name="lotCount"/> Lots
    /// (starting price ฿10,000). Close it with <c>api.Clock.SetUtcNow(auction.ClosesAt)</c> (ClockMoving tests only).
    /// </summary>
    public static async Task<(AuctionView Auction, List<LotView> Lots)> ShortAuctionAsync(this EshopApiFactory api, int lotCount)
    {
        var staff = await api.CreateStaffClientAsync();
        var now = api.Clock.GetUtcNow();
        var auction = await staff.CreateAuctionAsync(new { opensAt = now.AddHours(-1), closesAt = now.AddMinutes(1) });
        var lots = new List<LotView>();
        for (var i = 0; i < lotCount; i++)
        {
            lots.Add(await staff.AddLotAsync(auction.Id, new
            {
                title = $"iPhone 15 128GB Black #{i + 1}",
                conditionNotes = "ครบกล่อง",
                startingPrice = 10_000m,
                imageIds = new[] { await staff.UploadLotPhotoAsync(TestData.NewPhoto()) },
            }));
        }

        return (auction, lots);
    }

    /// <summary>A Lot as Staff see it in the back office (with the winner and payment in <c>Sale</c>).</summary>
    public static async Task<LotView> GetBackOfficeLotAsync(this HttpClient staff, int auctionId, int lotId)
    {
        var auction = (await staff.GetFromJsonAsync<AuctionView>($"/api/back-office/auctions/{auctionId}", Ct))!;
        return auction.Lots.Single(l => l.Id == lotId);
    }

    public static Task<HttpResponseMessage> RecordPaymentAsync(this HttpClient staff, int lotId, string branchCode) =>
        staff.PostAsJsonAsync($"/api/back-office/lots/{lotId}/payment", new { branchCode }, Ct);

    public static Task<HttpResponseMessage> ForfeitAsync(this HttpClient staff, int lotId) =>
        staff.PostAsync($"/api/back-office/lots/{lotId}/forfeit", null, Ct);

    public static Task<HttpResponseMessage> ReofferAsync(this HttpClient staff, int lotId, int auctionId, decimal? startingPrice = null) =>
        staff.PostAsJsonAsync($"/api/back-office/lots/{lotId}/re-offer", new { auctionId, startingPrice }, Ct);

    /// <summary>The signed-in Customer's won Lots, newest first.</summary>
    public static async Task<List<LotView>> GetWonLotsAsync(this HttpClient customer) =>
        (await customer.GetFromJsonAsync<List<LotView>>("/api/lots/won", Ct))!;

    /// <summary>
    /// A signed-in Customer who has passed ID Verification. The only way to pass it is to apply for hire purchase,
    /// so this applies (ID Verification passes, Credit Check fails) and waits for the Credit Decision.
    /// </summary>
    public static async Task<HttpClient> IdVerifiedCustomerAsync(this EshopApiFactory api, string? mobileNumber = null)
    {
        var customer = await api.CreateCustomerClientAsync(mobileNumber);
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "สมหญิง ประมูลเก่ง", new DateOnly(1990, 4, 1));
        api.CreditCheck.Fails(idNumber);

        var application = await customer.ApplyAndWaitAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));

        Assert.Equal("credit-check-failed", application.Reason);
        Assert.True((await customer.MeAsync()).IdVerified);
        return customer;
    }

    /// <summary>
    /// A signed-in Customer whose ID passed ID Verification but who turns 20 tomorrow: their Application was
    /// refused as under 20 (no Credit Decision), yet their ID counts as verified.
    /// </summary>
    public static async Task<HttpClient> IdVerifiedCustomerUnder20Async(this EshopApiFactory api)
    {
        var customer = await api.CreateCustomerClientAsync();
        var idNumber = TestData.NewIdNumber();
        var twentiethBirthdayTomorrow = Bangkok.Today(api.Clock.GetUtcNow()).AddYears(-20).AddDays(1);
        api.IdVerification.Verifies(idNumber, "เด็กชาย ประมูล", twentiethBirthdayTomorrow);

        var submitted = await customer.SubmitAcceptedAsync(new ApplicationForm(await customer.IPhone17Async(), idNumber));
        using var checkBack = await customer.WaitWhileReviewingAsync(submitted.Id);

        Assert.Equal(System.Net.HttpStatusCode.Gone, checkBack.StatusCode);
        Assert.True((await customer.MeAsync()).IdVerified);
        return customer;
    }

    public static Task<HttpResponseMessage> BidAsync(this HttpClient customer, int lotId, decimal amount) =>
        customer.PostAsJsonAsync($"/api/lots/{lotId}/bids", new { amount }, Ct);

    /// <summary>Expects a refusal with this status and ProblemDetails <c>error</c>, and returns the refusal.</summary>
    public static async Task<BidRefusal> ExpectRefusalAsync(this HttpResponseMessage response, System.Net.HttpStatusCode status, string error)
    {
        await response.ExpectAsync(status);
        var refusal = (await response.Content.ReadFromJsonAsync<BidRefusal>(Ct))!;
        Assert.Equal(error, refusal.Error);
        return refusal;
    }

    private sealed record PhotoView(string ImageId);

    private static async Task ExpectAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }
    }
}

// MinimumBid: only with bid-too-low.
internal sealed record BidRefusal(string? Error, decimal? MinimumBid, DateTimeOffset? PaymentDueAt);

internal sealed record AuctionView(int Id, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, string Status, List<LotView> Lots);

internal sealed record LotView(
    int Id,
    int AuctionId,
    string Title,
    string ConditionNotes,
    decimal StartingPrice,
    List<string> PhotoUrls,
    decimal? HighestBid,
    int BidCount,
    decimal MinimumBid,
    string AuctionStatus,
    bool? YouAreHighestBidder,
    string LotStatus,
    bool? YouWon,
    DateTimeOffset? PaymentDueAt,
    bool CashOnly,
    int? ReofferedAsLotId,
    LotSaleView? Sale);

internal sealed record LotSaleView(
    int WinnerCustomerId,
    string? WinnerFullName,
    string? WinnerMobileNumber,
    decimal Amount,
    DateTimeOffset PaymentDueAt,
    DateTimeOffset? PaidAt,
    string? PaymentBranchCode,
    string? PaymentRecordedBy,
    DateTimeOffset? ForfeitedAt,
    bool MayForfeit);
