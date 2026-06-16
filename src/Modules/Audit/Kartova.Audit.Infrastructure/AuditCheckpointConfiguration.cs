using Kartova.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Audit.Infrastructure;

internal sealed class AuditCheckpointConfiguration : IEntityTypeConfiguration<AuditCheckpoint>
{
    public void Configure(EntityTypeBuilder<AuditCheckpoint> builder)
    {
        builder.ToTable("audit_checkpoint");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Seq).HasColumnName("seq");
        builder.Property(x => x.RowHash).HasColumnName("row_hash").HasColumnType("bytea");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        // One checkpoint per (tenant, seq): rejects duplicate checkpoints if two creators race on
        // the same head, and gives the verify-from-checkpoint lookup an indexed path.
        builder.HasIndex(x => new { x.TenantId, x.Seq }).IsUnique().HasDatabaseName("ux_audit_checkpoint_tenant_seq");
    }
}
