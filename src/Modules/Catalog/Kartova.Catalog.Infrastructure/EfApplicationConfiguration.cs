using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfApplicationConfiguration : IEntityTypeConfiguration<Kartova.Catalog.Domain.Application>
{
    /// <summary>
    /// EF shadow-property name for the private <c>_id</c> backing field.
    /// Used by both this configuration and query handlers (e.g.,
    /// <see cref="ListApplicationsHandler"/>, <see cref="GetApplicationByIdHandler"/>)
    /// to avoid magic-string duplication.
    /// </summary>
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<Kartova.Catalog.Domain.Application> b)
    {
        b.ToTable("catalog_applications");

        // Map the private _id backing field (Guid) directly — no value converter needed.
        // EF can translate ORDER BY and WHERE expressions on a plain Guid property without
        // any converter gymnastics. The domain-typed Id property is a computed read-only
        // expression from _id and is ignored by EF. This pattern avoids the
        // InvalidOperationException / InvalidCastException that arise when EF tries to
        // translate `x.Id.Value` (ApplicationId → Guid value converter) in LINQ queries.
        // Map the private backing field _id directly as a plain Guid PK.
        // HasField("_id") tells EF Core to read/write the private field.
        // UsePropertyAccessMode(Field) ensures EF bypasses the computed Id property
        // (which has no setter) and accesses the field directly.
        b.Property<Guid>(IdFieldName)
            .HasField(IdFieldName)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasKey(IdFieldName);

        // The domain-typed Id getter (`ApplicationId Id => new(_id);`) has no setter
        // and EF currently ignores it by convention. Explicit Ignore guards against
        // future EF convention changes (e.g. complex/owned-type auto-mapping for
        // record-struct returns) that could otherwise cause silent model-snapshot drift.
        b.Ignore(x => x.Id);
        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_applications_tenant_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").IsRequired();
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.Lifecycle)
            .HasColumnName("lifecycle")
            .HasColumnType("smallint")
            .HasConversion<short>()                           // enum → smallint
            .HasDefaultValue(Lifecycle.Active)
            .IsRequired();

        b.Property(x => x.SunsetDate)
            .HasColumnName("sunset_date")
            .HasColumnType("timestamptz");                    // nullable by default

        b.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
    }
}
