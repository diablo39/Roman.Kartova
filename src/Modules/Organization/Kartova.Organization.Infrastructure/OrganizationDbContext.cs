using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class OrganizationDbContext : DbContext
{
    public OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : base(options) { }

    public DbSet<Kartova.Organization.Domain.Organization> Organizations => Set<Kartova.Organization.Domain.Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMembers => Set<TeamMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
