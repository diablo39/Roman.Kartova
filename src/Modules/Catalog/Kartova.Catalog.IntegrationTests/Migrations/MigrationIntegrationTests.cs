using Kartova.Catalog.Infrastructure;
using Kartova.Catalog.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests.Migrations;

[TestClass]
public class MigrationIntegrationTests
{
    private static PostgresFixture Pg { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        Pg = new PostgresFixture();
        await Pg.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task ClassDone()
    {
        if (Pg is not null) await Pg.DisposeAsync();
    }

    [TestMethod]
    public async Task Initial_Migration_Creates_Metadata_Table_With_Catalog_Row()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Pg.ConnectionString)
            .Options;

        await using var ctx = new CatalogDbContext(options);

        // Act
        await ctx.Database.MigrateAsync();

        // Assert — query raw to prove the table name and columns are literal.
        await using var conn = new NpgsqlConnection(Pg.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT module_name, schema_version FROM __kartova_metadata", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(string ModuleName, int Version)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(("catalog", 1), rows.Single());
    }

    [TestMethod]
    public async Task Migration_Is_Idempotent_On_Second_Run()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Pg.ConnectionString)
            .Options;

        // First run (may or may not already be applied by previous test — shared fixture).
        await using (var ctx1 = new CatalogDbContext(options))
        {
            await ctx1.Database.MigrateAsync();
        }

        // Second run — must be a no-op, no exception.
        await using var ctx2 = new CatalogDbContext(options);
        await ctx2.Database.MigrateAsync();

        // Row should still be unique (ON CONFLICT DO NOTHING).
        await using var conn = new NpgsqlConnection(Pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM __kartova_metadata WHERE module_name = 'catalog'", conn);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.AreEqual(1L, count);
    }
}
