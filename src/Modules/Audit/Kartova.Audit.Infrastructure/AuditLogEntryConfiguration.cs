using System.Text.Json;
using Kartova.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kartova.Audit.Infrastructure;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Seq).HasColumnName("seq");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        builder.Property(x => x.ActorType).HasColumnName("actor_type").HasConversion<string>();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        builder.Property(x => x.ActorDisplay).HasColumnName("actor_display");
        builder.Property(x => x.Action).HasColumnName("action");
        builder.Property(x => x.TargetType).HasColumnName("target_type");
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.PrevHash).HasColumnName("prev_hash");
        builder.Property(x => x.RowHash).HasColumnName("row_hash");

        // data: stored as jsonb (forensic queryability + write-time validation). The converter
        // round-trips through System.Text.Json; the chain hash is computed by the domain
        // canonical serializer (sorted keys, string values) so jsonb normalization is hash-neutral
        // (design spec §5). A null dictionary maps to SQL NULL.
        var dataConverter = new ValueConverter<IReadOnlyDictionary<string, string?>?, string?>(
            v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(v, (JsonSerializerOptions?)null));

        var dataComparer = new ValueComparer<IReadOnlyDictionary<string, string?>?>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => v);

        builder.Property(x => x.Data)
            .HasColumnName("data")
            .HasColumnType("jsonb")
            .HasConversion(dataConverter, dataComparer);

        builder.HasIndex(x => new { x.TenantId, x.Seq }).IsUnique().HasDatabaseName("ux_audit_log_tenant_seq");
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt }).HasDatabaseName("idx_audit_log_tenant_time");
        builder.HasIndex(x => new { x.TenantId, x.TargetType, x.TargetId }).HasDatabaseName("idx_audit_log_tenant_target");
    }
}
