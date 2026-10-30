using Eshop.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eshop.Data.Configurations;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.HasIndex(s => s.Username).IsUnique();
        builder.Property(s => s.Username).HasMaxLength(50);
        builder.Property(s => s.DisplayName).HasMaxLength(200);
        builder.Property(s => s.PasswordHash).HasMaxLength(200);
    }
}
