using Eshop.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eshop.Data.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasIndex(c => c.MobileNumber).IsUnique();
        builder.Property(c => c.MobileNumber).HasMaxLength(10);
        builder.Property(c => c.IdNumber).HasMaxLength(13);
        builder.Property(c => c.FullName).HasMaxLength(200);
        builder.Ignore(c => c.IsIdVerified);
    }
}
