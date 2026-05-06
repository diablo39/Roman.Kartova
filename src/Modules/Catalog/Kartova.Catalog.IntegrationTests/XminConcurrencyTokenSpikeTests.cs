using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Testcontainers.PostgreSql;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Spike — proves that EF Core can map the Postgres xmin system column as a
/// concurrency token (uint), and that a stale OriginalValue raises
/// DbUpdateConcurrencyException on SaveChangesAsync. If this passes, slice 5
/// adopts the same mapping in EfApplicationConfiguration (Task 6). If it fails,
/// fall back to an explicit `version BIGINT` column (spec §10 risks).
/// </summary>
public class XminConcurrencyTokenSpikeTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = null!;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:18")
            .Build();
        await _pg.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Xmin_advances_on_update_and_raises_on_stale_OriginalValue()
    {
        var cs = _pg.GetConnectionString();

        // First DbContext: create the table and insert one row.
        var optionsA = new DbContextOptionsBuilder<SpikeDbContext>().UseNpgsql(cs).Options;
        await using (var db = new SpikeDbContext(optionsA))
        {
            await db.Database.EnsureCreatedAsync();
            db.Widgets.Add(new SpikeWidget { Id = Guid.NewGuid(), Name = "alpha" });
            await db.SaveChangesAsync();
        }

        // Second DbContext A: load + capture the version.
        await using var dbA = new SpikeDbContext(optionsA);
        var rowA = await dbA.Widgets.FirstAsync();
        var versionAtLoad = rowA.Version;

        // Third DbContext B (separate scope, simulates another client): load + update.
        var optionsB = new DbContextOptionsBuilder<SpikeDbContext>().UseNpgsql(cs).Options;
        await using (var dbB = new SpikeDbContext(optionsB))
        {
            var rowB = await dbB.Widgets.FirstAsync();
            rowB.Name = "beta";
            await dbB.SaveChangesAsync();
        }

        // Now A tries to update with the stale captured OriginalValue.
        rowA.Name = "gamma";
        dbA.Entry(rowA).Property(w => w.Version).OriginalValue = versionAtLoad;

        var act = async () => await dbA.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private sealed class SpikeWidget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint Version { get; set; }
    }

    private sealed class SpikeDbContext(DbContextOptions<SpikeDbContext> opts) : DbContext(opts)
    {
        public DbSet<SpikeWidget> Widgets => Set<SpikeWidget>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<SpikeWidget>(e =>
            {
                e.ToTable("spike_widgets");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired();
                e.Property(x => x.Version)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsRowVersion()
                    .IsConcurrencyToken();
            });
        }
    }
}
