using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfEnvironmentConfiguration : IEntityTypeConfiguration<CatalogEnvironment>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<CatalogEnvironment> b)
    {
        b.ToTable("catalog_environments");

        b.Property<Guid>(IdFieldName)
            .HasField(IdFieldName)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasKey(IdFieldName);
        b.Ignore(x => x.Id);

        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_environments_tenant_id");

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        // Unique per tenant — environments are named deployment targets; duplicates are a 409.
        b.HasIndex(x => new { x.TenantId, x.DisplayName })
            .IsUnique()
            .HasDatabaseName("ux_catalog_environments_tenant_id_display_name");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4096).IsRequired();

        b.Property(x => x.Type)
            .HasColumnName("type")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.Region).HasColumnName("region").HasMaxLength(256);
        b.Property(x => x.Cluster).HasColumnName("cluster").HasMaxLength(256);

        b.Property(x => x.ResourceDetails)
            .HasColumnName("resource_details")
            .HasColumnType("jsonb")
            .IsRequired();

        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.Xmin)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
    }
}
