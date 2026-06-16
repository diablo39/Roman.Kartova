using Kartova.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// DbContext for cross-tenant audit maintenance (ADR-0105 checkpoint sweep). Uses a BYPASSRLS-role
/// connection, so RLS does not filter rows — it can read every tenant's chain head and existing
/// checkpoints to decide which tenants need a new checkpoint. NOT registered via
/// <c>AddModuleDbContext</c> and does NOT participate in <c>ITenantScope</c>; it is wired only in the
/// composition root and must never be injected into tenant-scoped code. Used read-only — checkpoint
/// writes go through the tenant-scoped path so the INSERT still passes RLS WITH CHECK
/// (see <see cref="AuditCheckpointHostedService"/>).
/// </summary>
public sealed class AdminAuditDbContext : DbContext
{
    public AdminAuditDbContext(DbContextOptions<AdminAuditDbContext> options) : base(options) { }

    public DbSet<AuditLogEntry> AuditEntries => Set<AuditLogEntry>();

    public DbSet<AuditCheckpoint> Checkpoints => Set<AuditCheckpoint>();

    // Reuse the tenant-scoped context's entity configurations (same tables, columns, indexes).
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
}
