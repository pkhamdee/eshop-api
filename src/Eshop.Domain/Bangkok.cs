namespace Eshop.Domain;

/// <summary>
/// Asia/Bangkok (UTC+7, no daylight saving): the time zone of every eShop date rule (age, Contract month,
/// Handover date, Auction schedule).
/// </summary>
public static class Bangkok
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    /// <summary><paramref name="instant"/> as Bangkok local time.</summary>
    public static DateTimeOffset LocalTime(DateTimeOffset instant) => instant.ToOffset(Offset);

    /// <summary>The Bangkok calendar date at <paramref name="instant"/>.</summary>
    public static DateOnly Today(DateTimeOffset instant) => DateOnly.FromDateTime(LocalTime(instant).DateTime);
}
