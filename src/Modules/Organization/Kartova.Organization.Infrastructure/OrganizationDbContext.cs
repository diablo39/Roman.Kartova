using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class OrganizationDbContext : DbContext
{
    public OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : base(options) { }

    public DbSet<Kartova.Organization.Domain.Organization> Organizations => Set<Kartova.Organization.Domain.Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
