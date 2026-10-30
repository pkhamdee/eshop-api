namespace Eshop.Domain.Auctions;

/// <summary>
/// A weekly, time-boxed sale of used iPhones in Lots, won by the highest Bid when it closes (GLOSSARY: Auction).
/// Bids are taken from <see cref="OpensAt"/> (inclusive) until <see cref="ClosesAt"/> (exclusive). No reserve price
/// and no automatic extension.
/// </summary>
public class Auction
{
    public int Id { get; set; }

    public DateTimeOffset OpensAt { get; set; }

    public DateTimeOffset ClosesAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Lot> Lots { get; set; } = [];

    public AuctionStatus StatusAt(DateTimeOffset now) =>
        now < OpensAt ? AuctionStatus.Upcoming
        : now < ClosesAt ? AuctionStatus.Open
        : AuctionStatus.Closed;
}

public enum AuctionStatus
{
    Upcoming,
    Open,
    Closed,
}

/// <summary>The weekly Auction schedule: opens Monday 10:00 and closes Sunday 20:00, Asia/Bangkok (UTC+7, no DST).</summary>
public static class AuctionSchedule
{
    private static readonly TimeSpan OpeningTime = TimeSpan.FromHours(10);

    /// <summary>From Monday 10:00 to Sunday 20:00.</summary>
    public static readonly TimeSpan Length = TimeSpan.FromDays(6) + TimeSpan.FromHours(10);

    /// <summary>The next weekly Auction that opens strictly after <paramref name="now"/>.</summary>
    public static (DateTimeOffset OpensAt, DateTimeOffset ClosesAt) Next(DateTimeOffset now)
    {
        var local = Bangkok.LocalTime(now);
        var daysToMonday = ((int)DayOfWeek.Monday - (int)local.DayOfWeek + 7) % 7;
        var opensAt = new DateTimeOffset(local.Date, Bangkok.Offset).AddDays(daysToMonday) + OpeningTime;
        if (opensAt <= now)
        {
            opensAt = opensAt.AddDays(7);
        }

        return (opensAt.ToUniversalTime(), (opensAt + Length).ToUniversalTime());
    }
}
