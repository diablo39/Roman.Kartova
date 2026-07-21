using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfRelationshipConfiguration : IEntityTypeConfiguration<Relationship>
{
    internal const string IdFieldName = "_id";

    // Drift hardening: relationships.type is persisted as a string. A row whose value
    // is not a current RelationshipType member (e.g. a genuinely-unknown legacy string
    // left by data drift) would throw at EF materialization and 500 every read over the
    // tenant's relationships. Excluding unmappable rows at the SQL layer (type IN (...))
    // makes all read paths (list / graph / api-surface) tolerant. Insert-time validation
    // still prevents new unknown types; this guards against pre-existing drift.
    // 'PartOf' is now a valid, visible relationship type (System grouping, E-03.F-03) —
    // it is no longer excluded by this filter; only truly-unknown strings are.
    private static readonly RelationshipType[] KnownRelationshipTypes = Enum.GetValues<RelationshipType>();

    public void Configure(EntityTypeBuilder<Relationship> b)
    {
        b.ToTable("relationships");

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

        b.ComplexProperty(x => x.Source, s =>
        {
            s.Property(p => p.Kind).HasColumnName("source_kind").HasConversion<string>().HasMaxLength(64).IsRequired();
            s.Property(p => p.Id).HasColumnName("source_id").IsRequired();
        });
        b.ComplexProperty(x => x.Target, t =>
        {
            t.Property(p => p.Kind).HasColumnName("target_kind").HasConversion<string>().HasMaxLength(64).IsRequired();
            t.Property(p => p.Id).HasColumnName("target_id").IsRequired();
        });

        b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        // Indexes for relationships are declared in the AddRelationships migration, not here:
        // EF 10 cannot reference ComplexProperty (Source/Target) columns by name in HasIndex.

        b.HasQueryFilter(r => KnownRelationshipTypes.Contains(r.Type));
    }
}
