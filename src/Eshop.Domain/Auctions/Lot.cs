using Eshop.Domain.Branches;
using Eshop.Domain.Customers;

namespace Eshop.Domain.Auctions;

/// <summary>
/// One used iPhone, complete with its box, offered in an Auction (GLOSSARY: Lot).
/// <see cref="HighestBid"/> / <see cref="HighestBidderId"/> always describe the current highest Bid; they are only ever
/// raised by a single conditional UPDATE (see eshop-api's Bid endpoint), which is what makes ties impossible.
/// </summary>
public class Lot
{
    /// <summary>Bids must beat the current highest Bid by at least this much (baht).</summary>
    public const decimal MinimumIncrement = 100m;

    public int Id { get; set; }

    public int AuctionId { get; set; }

    public Auction Auction { get; set; } = null!;

    /// <summary>Model, capacity and colour, e.g. "iPhone 15 Pro 256GB Natural Titanium".</summary>
    public required string Title { get; set; }

    /// <summary>Condition notes shown to bidders (scratches, battery health, what is in the box).</summary>
    public required string ConditionNotes { get; set; }

    /// <summary>The lowest acceptable first Bid (baht).</summary>
    public decimal StartingPrice { get; set; }

    /// <summary>IDs of the Lot's photos in the image service's public area, in display order.</summary>
    public List<string> ImageIds { get; set; } = [];

    public decimal? HighestBid { get; set; }

    public int? HighestBidderId { get; set; }

    public Customer? HighestBidder { get; set; }

    public int BidCount { get; set; }

    public List<Bid> Bids { get; set; } = [];

    /// <summary>When Staff recorded the winner's cash payment and Handover at a Branch; null until then.</summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>The Branch where the winner paid cash and took the Lot.</summary>
    public int? PaymentBranchId { get; set; }

    public Branch? PaymentBranch { get; set; }

    /// <summary>Username of the Staff member who recorded the payment.</summary>
    public string? PaymentRecordedBy { get; set; }

    /// <summary>When Staff marked the Lot Forfeited (the winner did not pay within 3 days); null otherwise.</summary>
    public DateTimeOffset? ForfeitedAt { get; set; }

    /// <summary>The new Lot that offers this (Forfeited or unsold) Lot again in a later Auction; null if not yet.</summary>
    public int? ReofferedAsLotId { get; set; }

    /// <summary>The smallest Bid accepted now: the starting price for the first Bid, else highest + ฿100.</summary>
    public decimal MinimumBid => MinimumBidOver(HighestBid, StartingPrice);

    /// <summary>
    /// The Lot's state at <paramref name="now"/>; <see cref="Auction"/> must be loaded. When the Auction closes, the
    /// highest Bid (<see cref="HighestBidderId"/> at <see cref="HighestBid"/>) wins and nobody else ever does: a
    /// Forfeited Lot does not pass to the second-highest bidder.
    /// </summary>
    public LotStatus StatusAt(DateTimeOffset now) =>
        Auction.StatusAt(now) != AuctionStatus.Closed ? LotStatus.InAuction
        : HighestBidderId is null ? LotStatus.Unsold
        : PaidAt is not null ? LotStatus.Sold
        : ForfeitedAt is not null ? LotStatus.Forfeited
        : LotStatus.AwaitingPayment;

    /// <summary>The winner must pay cash at a Branch by this time (close + 3 days); null if there is no winner.</summary>
    public DateTimeOffset? PaymentDueAt => HighestBidderId is null ? null : Auction.ClosesAt + PaymentWindow;

    /// <summary>The winner's Customer id once the Auction has closed; null before the close or with no Bids.</summary>
    public int? WinnerIdAt(DateTimeOffset now) => Auction.StatusAt(now) == AuctionStatus.Closed ? HighestBidderId : null;

    /// <summary>Staff may mark the Lot Forfeited once the 3 days have passed (from the due time on) without payment.</summary>
    public bool MayBeForfeitedAt(DateTimeOffset now) => StatusAt(now) == LotStatus.AwaitingPayment && now >= PaymentDueAt;

    /// <summary>A Forfeited or unsold Lot may be offered again in a later Auction, once.</summary>
    public bool MayBeReofferedAt(DateTimeOffset now) =>
        StatusAt(now) is LotStatus.Forfeited or LotStatus.Unsold && ReofferedAsLotId is null;

    /// <summary>How long the winner has to pay cash at a Branch after the Auction closes.</summary>
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromDays(3);

    public static decimal MinimumBidOver(decimal? highestBid, decimal startingPrice) =>
        highestBid is { } highest ? highest + MinimumIncrement : startingPrice;
}

/// <summary>
/// InAuction until the Auction closes; then Unsold (no Bids) or AwaitingPayment (the winner has 3 days to pay cash at
/// a Branch), ending Sold (paid and handed over) or Forfeited (Staff marked it after 3 days unpaid).
/// </summary>
public enum LotStatus
{
    InAuction,
    Unsold,
    AwaitingPayment,
    Sold,
    Forfeited,
}

/// <summary>A Customer's offer for a Lot (GLOSSARY: Bid). Accepted Bids only; refused ones are not stored.</summary>
public class Bid
{
    public long Id { get; set; }

    public int LotId { get; set; }

    public int CustomerId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset PlacedAt { get; set; }
}
