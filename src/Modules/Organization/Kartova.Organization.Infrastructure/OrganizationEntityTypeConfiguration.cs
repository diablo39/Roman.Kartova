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

        // B1 placeholder: Description, Logo, and DefaultTimeZone members are added on the
        // aggregate in slice 9 Task B1, but their column wiring + EF owned-type config for
        // OrgLogo lands in Task B4 alongside the migration. Until then we ignore them so
        // model validation passes and existing queries against the slice-2 schema keep working.
        builder.Ignore(x => x.Description);
        builder.Ignore(x => x.Logo);
        builder.Ignore(x => x.DefaultTimeZone);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_organizations_tenant");

        // No global query filter: tenant isolation is enforced by Postgres RLS (ADR-0012/0090).
    }
}
