using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

/// <summary>EF Core mapping for <see cref="CatalogSystem"/> (design §4.4). Mirrors
/// <see cref="EfApiConfiguration"/>'s structure; <c>Description</c> is nullable here
/// (System has no required description, unlike Api).</summary>
public sealed class EfSystemConfiguration : IEntityTypeConfiguration<CatalogSystem>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<CatalogSystem> b)
    {
        b.ToTable("catalog_systems");

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
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_systems_tenant_id");

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.DisplayName })
            .HasDatabaseName("ix_catalog_systems_tenant_id_display_name");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4096);

        b.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
        b.HasIndex(x => x.TeamId).HasDatabaseName("idx_catalog_systems_team");
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
