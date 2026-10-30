using System.Security.Claims;
using Eshop.Api.Auth;
using Eshop.Api.Images;
using Eshop.Domain.Auctions;

namespace Eshop.Api.Auctions;

// Status: upcoming | open | closed (from the clock: Bids are taken from opensAt until, not including, closesAt).
// Lots[].Sale is filled only in back-office responses (forStaff).
internal sealed record AuctionResponse(int Id, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, string Status, List<LotResponse> Lots)
{
    public static AuctionResponse Of(Auction auction, DateTimeOffset now, ClaimsPrincipal? user = null, bool forStaff = false) =>
        new(auction.Id, auction.OpensAt, auction.ClosesAt, StatusCode(auction.StatusAt(now)),
            auction.Lots.OrderBy(l => l.Id).Select(l => LotResponse.Of(l, auction, now, user, forStaff)).ToList());

    public static string StatusCode(AuctionStatus status) => status switch
    {
        AuctionStatus.Upcoming => "upcoming",
        AuctionStatus.Open => "open",
        _ => "closed",
    };
}

// HighestBid is null until the first Bid; MinimumBid is the smallest Bid accepted now (starting price, or highest + ฿100).
// YouAreHighestBidder / YouWon: only for a signed-in Customer (null otherwise). Pages poll GET /api/lots/{id} for these.
// LotStatus: in-auction | unsold | awaiting-payment | sold | forfeited. PaymentDueAt: close + 3 days once there is a
// winner. CashOnly is always true: won Lots are paid in cash at a Branch, never on hire purchase.
// ReofferedAsLotId: the Lot that offers this one again in a later Auction. Sale: back office only.
internal sealed record LotResponse(
    int Id,
    int AuctionId,
    string Title,
    string ConditionNotes,
    decimal StartingPrice,
    List<string> ImageIds,
    List<string> PhotoUrls,
    decimal? HighestBid,
    int BidCount,
    decimal MinimumBid,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    string AuctionStatus,
    bool? YouAreHighestBidder,
    string LotStatus,
    bool? YouWon,
    DateTimeOffset? PaymentDueAt,
    bool CashOnly,
    int? ReofferedAsLotId,
    LotSaleResponse? Sale)
{
    public static LotResponse Of(Lot lot, Auction auction, DateTimeOffset now, ClaimsPrincipal? user = null, bool forStaff = false)
    {
        lot.Auction = auction;
        var customerId = user?.CustomerId();
        var status = lot.StatusAt(now);
        var winnerId = lot.WinnerIdAt(now);
        return new(lot.Id, auction.Id, lot.Title, lot.ConditionNotes, lot.StartingPrice, lot.ImageIds,
            lot.ImageIds.Select(ImageTypes.PublicUrl).ToList(), lot.HighestBid, lot.BidCount, lot.MinimumBid,
            auction.OpensAt, auction.ClosesAt, AuctionResponse.StatusCode(auction.StatusAt(now)),
            customerId is null ? null : lot.HighestBidderId == customerId,
            StatusCode(status),
            customerId is null ? null : winnerId == customerId,
            winnerId is null ? null : lot.PaymentDueAt,
            CashOnly: true,
            lot.ReofferedAsLotId,
            forStaff && winnerId is not null ? LotSaleResponse.Of(lot, now) : null);
    }

    public static string StatusCode(LotStatus status) => status switch
    {
        Domain.Auctions.LotStatus.InAuction => "in-auction",
        Domain.Auctions.LotStatus.Unsold => "unsold",
        Domain.Auctions.LotStatus.AwaitingPayment => "awaiting-payment",
        Domain.Auctions.LotStatus.Sold => "sold",
        _ => "forfeited",
    };
}

// The winner and what happened after the close, for Staff at the Branch counter. Winner details need
// Lot.HighestBidder (and PaymentBranch once paid) loaded. MayForfeit: the 3 days have passed without payment.
internal sealed record LotSaleResponse(
    int WinnerCustomerId,
    string? WinnerFullName,
    string? WinnerMobileNumber,
    decimal Amount,
    DateTimeOffset PaymentDueAt,
    DateTimeOffset? PaidAt,
    string? PaymentBranchCode,
    string? PaymentRecordedBy,
    DateTimeOffset? ForfeitedAt,
    bool MayForfeit)
{
    public static LotSaleResponse Of(Lot lot, DateTimeOffset now) =>
        new(lot.HighestBidderId!.Value, lot.HighestBidder?.FullName, lot.HighestBidder?.MobileNumber, lot.HighestBid!.Value,
            lot.PaymentDueAt!.Value, lot.PaidAt, lot.PaymentBranch?.Code, lot.PaymentRecordedBy, lot.ForfeitedAt,
            lot.MayBeForfeitedAt(now));
}
