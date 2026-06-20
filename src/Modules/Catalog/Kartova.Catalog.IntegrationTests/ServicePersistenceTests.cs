using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.Catalog.IntegrationTests.Fixtures;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ServicePersistenceTests
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
    public async Task Endpoints_roundtrip_through_jsonb_and_empty_list_is_not_null()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(Pg.ConnectionString).Options;
        await using (var ctx = new CatalogDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        var tenant = new TenantId(Guid.NewGuid());
        // SET LOCAL the tenant guc so RLS lets the insert/select through (the test
        // talks to the DbContext directly, outside the API's tenant-scope middleware).
        var withEndpoints = Service.Create("svc-a", "with endpoints", Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ServiceEndpoint("https://a.example.com", Protocol.Rest),
                    new ServiceEndpoint("https://b.example.com", Protocol.Grpc) },
            tenant, DateTimeOffset.UtcNow);
        var noEndpoints = Service.Create("svc-b", "no endpoints", Guid.NewGuid(), Guid.NewGuid(),
            Array.Empty<ServiceEndpoint>(), tenant, DateTimeOffset.UtcNow);

        await using (var ctx = new CatalogDbContext(options))
        {
            await ctx.Database.OpenConnectionAsync();
            await SetTenantAsync(ctx, tenant.Value);
            ctx.Services.AddRange(withEndpoints, noEndpoints);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new CatalogDbContext(options))
        {
            await ctx.Database.OpenConnectionAsync();
            await SetTenantAsync(ctx, tenant.Value);
            var a = await ctx.Services.SingleAsync(s => s.DisplayName == "svc-a");
            var b = await ctx.Services.SingleAsync(s => s.DisplayName == "svc-b");

            Assert.AreEqual(2, a.Endpoints.Count);
            Assert.AreEqual(Protocol.Grpc, a.Endpoints[1].Protocol);
            Assert.IsNotNull(b.Endpoints);
            Assert.AreEqual(0, b.Endpoints.Count);
            Assert.AreEqual(HealthStatus.Unknown, b.Health);
        }
    }

    private static async Task SetTenantAsync(CatalogDbContext ctx, Guid tenant)
    {
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SET app.current_tenant_id = '{tenant}'";
        await cmd.ExecuteNonQueryAsync();
    }
}
