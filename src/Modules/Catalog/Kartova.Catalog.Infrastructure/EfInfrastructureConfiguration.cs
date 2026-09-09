using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfInfrastructureConfiguration : IEntityTypeConfiguration<InfrastructureResource>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<InfrastructureResource> b)
    {
        b.ToTable("catalog_infrastructure");

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
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_infrastructure_tenant_id");

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.DisplayName })
            .HasDatabaseName("ix_catalog_infrastructure_tenant_id_display_name");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4096).IsRequired();

        // nullable free string (ADR-0115 slice 2a). Deliberate deviation from spec §3 #2 / §5.2
        // ("provider text NULL"): varchar(256) enforces the domain's 256-char cap at the DB
        // level too, not just in the entity — chosen over the spec's plain text for that reason.
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(256);

        b.Property(x => x.Type)
            .HasColumnName("type")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.SystemId).HasColumnName("system_id");   // nullable, unwritten in slice 1

        b.Property(x => x.Attributes)
            .HasColumnName("attributes")
            .HasColumnType("jsonb")
            .IsRequired();   // opaque payload (Audit pattern); GIN index added via raw SQL in migration

        b.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
        b.HasIndex(x => x.TeamId).HasDatabaseName("idx_catalog_infrastructure_team");
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
