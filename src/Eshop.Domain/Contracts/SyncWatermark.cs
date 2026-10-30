namespace Eshop.Domain.Contracts;

/// <summary>
/// How far eshop-sync-master has pulled one Enterprise System table: the highest <c>row_ver</c> (SQL Server
/// rowversion, as a number) already copied. The next pull asks only for rows changed after it.
/// </summary>
public class SyncWatermark
{
    /// <summary>The Enterprise table, e.g. <c>hp.contract</c>.</summary>
    public required string Source { get; set; }

    public long LastRowVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
