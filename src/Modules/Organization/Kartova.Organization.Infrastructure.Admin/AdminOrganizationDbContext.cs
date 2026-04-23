using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// DbContext for the admin bypass path (POST /api/v1/admin/organizations).
/// Uses a connection string with a BYPASSRLS role, so RLS policies do not filter rows.
/// NOT registered via AddModuleDbContext — does NOT participate in ITenantScope.
/// </summary>
public sealed class AdminOrganizationDbContext : DbContext
{
    public AdminOrganizationDbContext(DbContextOptions<AdminOrganizationDbContext> options) : base(options) { }

    public DbSet<Kartova.Organization.Domain.Organization> Organizations => Set<Kartova.Organization.Domain.Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Reuse the same configuration as the tenant-scoped DbContext.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
