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

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_organizations_tenant");

        // Defense-in-depth per ADR-0012: app-layer filter paired with DB-level RLS policy.
        // The tenant id for the filter is the *connection-level* GUC; we can't read it from EF,
        // so we instead rely on RLS + an explicit query where callers want strict id-matching.
        // No global query filter here because RLS already enforces tenant isolation server-side.
    }
}
