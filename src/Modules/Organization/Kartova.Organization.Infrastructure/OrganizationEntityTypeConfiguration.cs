using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationEntityTypeConfiguration : IEntityTypeConfiguration<Kartova.Organization.Domain.Organization>
{
    public void Configure(EntityTypeBuilder<Kartova.Organization.Domain.Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, g => new OrganizationId(g));

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(t => t.Value, g => new TenantId(g));

        builder.Property(x => x.DisplayName)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1024);
        builder.Property(x => x.DefaultTimeZone).HasColumnName("default_time_zone").HasMaxLength(64).IsRequired().HasDefaultValue("UTC");
        builder.OwnsOne(x => x.Logo, l =>
        {
            l.Property(p => p.Bytes).HasColumnName("logo_bytes");
            l.Property(p => p.MimeType).HasColumnName("logo_mime_type").HasMaxLength(32);
            l.Property(p => p.ContentHash).HasColumnName("logo_content_hash").HasMaxLength(64);
        });

        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_organizations_tenant");

        // No global query filter: tenant isolation is enforced by Postgres RLS (ADR-0012/0090).
    }
}
