using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(v => v.Value, v => new TenantId(v));
        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(x => x.GivenName).HasColumnName("given_name").HasMaxLength(128);
        b.Property(x => x.FamilyName).HasColumnName("family_name").HasMaxLength(128);
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasIndex(x => new { x.TenantId, x.Email })
            .IsUnique()
            .HasDatabaseName("ux_users_tenant_email");
        b.HasIndex(x => x.TenantId).HasDatabaseName("idx_users_tenant");
    }
}
