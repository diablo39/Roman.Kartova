using FluentAssertions;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Probe: does EF Core 10 + Npgsql auto-enlist in a connection's active transaction
/// when the DbContext is configured via UseNpgsql(connection)?
///
/// Outcome decides whether EnlistInTenantScopeInterceptor (spec §3.1) ships.
/// - Pass → EF auto-enlists; interceptor not needed.
/// - Fail → ship the interceptor.
/// </summary>
[Collection(KartovaApiCollection.Name)]
public class EfEnlistmentProbeTests
{
    private readonly KartovaApiFixture _fx;

    public EfEnlistmentProbeTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task DbContext_writes_inside_scope_are_rolled_back_on_scope_dispose()
    {
        // If EF is enlisted in the scope's tx, the scope's DisposeAsync rollback must
        // drop the row. If EF runs its own tx (or no tx), the row commits and persists.
        var rowName = $"Probe-{Guid.NewGuid()}";
        var rowId = Guid.NewGuid();

        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();

        await using (var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default))
        {
            var db = sp.GetRequiredService<OrganizationDbContext>();
            var org = OrganizationTestHelper.CreateWithTenant(rowId, SeededOrgs.OrgA, rowName);
            db.Add(org);
            await db.SaveChangesAsync();

            // Exit without CommitAsync → handle.DisposeAsync rolls back.
        }

        await using var bypass = new NpgsqlConnection(_fx.BypassConnectionString);
        await bypass.OpenAsync();
        await using var cmd = bypass.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM organizations WHERE name = $1";
        cmd.Parameters.AddWithValue(rowName);
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(0,
            because: "if EF is enlisted in the scope's transaction, scope rollback drops the row. " +
                     "Non-zero count means EF committed independently (interceptor needed).");
    }

    [Fact]
    public async Task DbContext_CurrentTransaction_matches_scope_transaction()
    {
        // Direct verification via EF's public API: the DbContext should report the
        // scope's NpgsqlTransaction as its current ambient transaction.
        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        var npgScope = (INpgsqlTenantScope)tenantScope;

        await using var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);

        var db = sp.GetRequiredService<OrganizationDbContext>();

        // Trigger the connection to be associated with the DbContext.
        await db.Database.OpenConnectionAsync();

        var efTx = db.Database.CurrentTransaction;
        efTx.Should().NotBeNull(
            because: "DbContext should report enlistment in the scope's active transaction");
        efTx!.GetDbTransaction().Should().BeSameAs(npgScope.Transaction,
            because: "DbContext's CurrentTransaction must be the SAME instance as the scope's transaction");
    }
}
