using Eshop.Domain.Auctions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eshop.Data.Configurations;

internal sealed class AuctionConfiguration : IEntityTypeConfiguration<Auction>
{
    public void Configure(EntityTypeBuilder<Auction> builder)
    {
        builder.HasIndex(a => a.OpensAt);
        builder.HasIndex(a => a.ClosesAt);
    }
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> builder)
    {
        builder.Property(l => l.Title).HasMaxLength(200);
        builder.Property(l => l.ConditionNotes).HasMaxLength(2000);
        builder.Property(l => l.StartingPrice).HasPrecision(12, 2);
        builder.Property(l => l.HighestBid).HasPrecision(12, 2);
        builder.Property(l => l.ImageIds).HasColumnType("text[]");
        builder.Ignore(l => l.MinimumBid);
        builder.Ignore(l => l.PaymentDueAt);
        builder.Property(l => l.PaymentRecordedBy).HasMaxLength(100);
        builder.HasOne(l => l.PaymentBranch).WithMany().HasForeignKey(l => l.PaymentBranchId).OnDelete(DeleteBehavior.Restrict);
        // A Lot is offered again at most once (the new Lot may itself be offered again later).
        builder.HasOne<Lot>().WithOne().HasForeignKey<Lot>(l => l.ReofferedAsLotId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Auction).WithMany(a => a.Lots).HasForeignKey(l => l.AuctionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.HighestBidder).WithMany().HasForeignKey(l => l.HighestBidderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> builder)
    {
        builder.Property(b => b.Amount).HasPrecision(12, 2);
        // A second line of defence against ties: two Bids of the same amount on one Lot can never both be stored.
        builder.HasIndex(b => new { b.LotId, b.Amount }).IsUnique();
        builder.HasIndex(b => b.CustomerId);

        builder.HasOne<Lot>().WithMany(l => l.Bids).HasForeignKey(b => b.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Eshop.Domain.Customers.Customer>().WithMany().HasForeignKey(b => b.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
