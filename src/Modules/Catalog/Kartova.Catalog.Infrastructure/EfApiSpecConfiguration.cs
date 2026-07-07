using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfApiSpecConfiguration : IEntityTypeConfiguration<ApiSpec>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<ApiSpec> b)
    {
        b.ToTable("catalog_api_specs");

        // Map the private _id backing field (Guid) directly — same pattern as
        // EfApiConfiguration, avoids EF trying to translate the computed Id getter.
        b.Property<Guid>(IdFieldName)
            .HasField(IdFieldName)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasKey(IdFieldName);
        b.Ignore(x => x.Id);

        b.Property(x => x.ApiId)
            .HasConversion(v => v.Value, v => new ApiId(v))
            .HasColumnName("api_id")
            .IsRequired();
        b.HasIndex(x => x.ApiId).IsUnique().HasDatabaseName("ux_catalog_api_specs_api_id");

        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_api_specs_tenant_id");

        b.Property(x => x.Content).HasColumnName("content").HasColumnType("text").IsRequired();
        b.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(64).IsRequired();
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
