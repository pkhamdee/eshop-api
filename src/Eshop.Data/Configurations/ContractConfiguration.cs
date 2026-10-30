using Eshop.Domain.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eshop.Data.Configurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.HasIndex(c => c.Number).IsUnique();
        builder.HasIndex(c => c.OfferId).IsUnique();
        builder.HasIndex(c => c.ApplicationId).IsUnique();

        // At most one active Contract per Customer, enforced by the database too (concurrent accepts).
        builder.HasIndex(c => c.CustomerId)
            .IsUnique()
            .HasFilter("status IN ('Active', 'Overdue')")
            .HasDatabaseName("ix_contracts_customer_id_active");

        builder.Property(c => c.Number).HasMaxLength(16);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Price).HasPrecision(12, 2);
        builder.Property(c => c.FlatRate).HasPrecision(6, 4);
        builder.Property(c => c.DownPayment).HasPrecision(12, 2);
        builder.Property(c => c.Installment).HasPrecision(12, 2);
        builder.Property(c => c.TotalPayable).HasPrecision(12, 2);
        builder.HasMany(c => c.Payments).WithOne(p => p.Contract).HasForeignKey(p => p.ContractId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Offer).WithOne().HasForeignKey<Contract>(c => c.OfferId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Application).WithOne().HasForeignKey<Contract>(c => c.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContractNumberCounterConfiguration : IEntityTypeConfiguration<ContractNumberCounter>
{
    public void Configure(EntityTypeBuilder<ContractNumberCounter> builder)
    {
        builder.HasKey(c => c.Month);
        builder.Property(c => c.Month).HasMaxLength(6);
    }
}

internal sealed class ContractOutboxEntryConfiguration : IEntityTypeConfiguration<ContractOutboxEntry>
{
    public void Configure(EntityTypeBuilder<ContractOutboxEntry> builder)
    {
        builder.ToTable("contract_outbox");
        builder.HasIndex(e => e.ContractNumber).IsUnique();
        // sync-master reads the queue: rows not yet pushed, oldest first.
        builder.HasIndex(e => e.EnqueuedAt).HasFilter("pushed_at IS NULL").HasDatabaseName("ix_contract_outbox_pending");
        builder.Property(e => e.ContractNumber).HasMaxLength(16);

        builder.HasOne(e => e.Contract).WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasIndex(p => p.EnterprisePaymentId).IsUnique();
        builder.HasIndex(p => p.ContractId);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Channel).HasMaxLength(10);
    }
}

internal sealed class SyncWatermarkConfiguration : IEntityTypeConfiguration<SyncWatermark>
{
    public void Configure(EntityTypeBuilder<SyncWatermark> builder)
    {
        builder.HasKey(w => w.Source);
        builder.Property(w => w.Source).HasMaxLength(50);
    }
}
