using Kartova.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditLogEntry> AuditEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
}
