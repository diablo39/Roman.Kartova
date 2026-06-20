using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfServiceConfiguration : IEntityTypeConfiguration<Service>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("catalog_services");

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
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_services_tenant_id");

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.DisplayName })
            .HasDatabaseName("ix_catalog_services_tenant_id_display_name");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4096).IsRequired();
        b.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
        b.HasIndex(x => x.TeamId).HasDatabaseName("idx_catalog_services_team");
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.Health)
            .HasColumnName("health")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .HasDefaultValue(HealthStatus.Unknown)
            .IsRequired();

        // Owned collection serialized to a single jsonb column. EF rehydrates each
        // element through the ServiceEndpoint(url, protocol) constructor (param names
        // match Url/Protocol). An empty collection round-trips as `[]`.
        b.OwnsMany(x => x.Endpoints, nav =>
        {
            nav.ToJson("endpoints");
            nav.Property(e => e.Url).HasJsonPropertyName("url");
            nav.Property(e => e.Protocol).HasConversion<short>().HasJsonPropertyName("protocol");
        });

        b.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
    }
}
