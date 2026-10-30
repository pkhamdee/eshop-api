using Eshop.Domain.Handovers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eshop.Data.Configurations;

internal sealed class HandoverConfiguration : IEntityTypeConfiguration<Handover>
{
    public void Configure(EntityTypeBuilder<Handover> builder)
    {
        builder.HasIndex(h => h.ContractId).IsUnique();
        builder.Property(h => h.Stage).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.StageChangedBy).HasMaxLength(50);
        builder.Property(h => h.DownPaymentReceived).HasPrecision(12, 2);
        builder.Property(h => h.DownPaymentReceivedBy).HasMaxLength(50);
        builder.Ignore(h => h.HandedOverOn);
        builder.Ignore(h => h.IsStopped);
        builder.Ignore(h => h.DownPaymentOutstanding);
        builder.Ignore(h => h.NextStage);

        // Beside the Contract, never on it: the Contract row stays as pushed (ADR-0001, ADR-0004).
        builder.HasOne(h => h.Contract).WithOne().HasForeignKey<Handover>(h => h.ContractId).OnDelete(DeleteBehavior.Restrict);
    }
}
