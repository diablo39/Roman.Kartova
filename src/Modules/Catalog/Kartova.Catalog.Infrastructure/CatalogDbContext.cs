using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// EF Core context for the Catalog module. Owns <see cref="KartovaMetadata"/>
/// and the <see cref="Kartova.Catalog.Domain.Application"/> aggregate; further
/// catalog aggregates land here as the module grows.
/// </summary>
public sealed class CatalogDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to configure the context.</param>
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    internal DbSet<KartovaMetadata> Metadata => Set<KartovaMetadata>();

    public DbSet<Kartova.Catalog.Domain.Application> Applications => Set<Kartova.Catalog.Domain.Application>();

    public DbSet<Kartova.Catalog.Domain.Service> Services => Set<Kartova.Catalog.Domain.Service>();

    public DbSet<Kartova.Catalog.Domain.Api> Apis => Set<Kartova.Catalog.Domain.Api>();

    public DbSet<Kartova.Catalog.Domain.Relationship> Relationships => Set<Kartova.Catalog.Domain.Relationship>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KartovaMetadata>(entity =>
        {
            entity.ToTable("__kartova_metadata");
            entity.HasKey(m => m.ModuleName);

            entity.Property(m => m.ModuleName)
                .HasColumnName("module_name")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(m => m.SchemaVersion)
                .HasColumnName("schema_version")
                .IsRequired();

            entity.Property(m => m.AppliedAt)
                .HasColumnName("applied_at")
                .IsRequired();
        });

        modelBuilder.ApplyConfiguration(new EfApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new EfServiceConfiguration());
        modelBuilder.ApplyConfiguration(new EfApiConfiguration());
        modelBuilder.ApplyConfiguration(new EfRelationshipConfiguration());
    }
}
