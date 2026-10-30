using Eshop.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eshop.Data.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.HasIndex(a => a.CustomerId);
        builder.HasIndex(a => a.IdNumber);
        builder.HasIndex(a => a.Status);

        builder.Property(a => a.Price).HasPrecision(12, 2);
        builder.Property(a => a.FlatRate).HasPrecision(6, 4);
        builder.Property(a => a.DownPayment).HasPrecision(12, 2);
        builder.Property(a => a.Installment).HasPrecision(12, 2);
        builder.Property(a => a.TotalPayable).HasPrecision(12, 2);
        builder.Property(a => a.CreditLimit).HasPrecision(12, 2);
        builder.Property(a => a.Handover).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.DeliveryAddress).HasMaxLength(500);
        builder.Property(a => a.DeliveryProvince).HasMaxLength(100);
        builder.Property(a => a.IdNumber).HasMaxLength(13);
        builder.Property(a => a.IdCardImageId).HasMaxLength(100);
        builder.Property(a => a.IdCardContentType).HasMaxLength(50);
        builder.Property(a => a.FullName).HasMaxLength(200);

        builder.Ignore(a => a.DecisionDeadline);
        builder.Ignore(a => a.FinancedAmount);
        builder.Ignore(a => a.ReapplyFrom);

        builder.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Branch).WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
