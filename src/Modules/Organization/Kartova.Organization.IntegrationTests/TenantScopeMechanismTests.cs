using System.Reflection;
using FluentAssertions;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Spec §6.3 defense-in-depth tests for the ITenantScope mechanism (ADR-0090).
/// These three tests close the gap noted in the post-merge reviews:
///   1. NoTenantScope_QueryThrowsFromInterceptor
///   2. CommitFailsAfterHandler_Returns500_NoDataCommitted (component-level)
///   3. ExceptionDuringHandler_RollsBack
///
/// Tests 2 and 3 drive ITenantScope directly rather than through the HTTP pipeline
/// because no tenant-scoped write endpoint exists in Slice 2 (organizations are
/// only written via the BYPASSRLS admin path). The mechanism contract is the same
/// regardless of caller; the HTTP-level wrapper is exercised by the existing
/// integration suite.
/// </summary>
[Collection(KartovaApiCollection.Name)]
public class TenantScopeMechanismTests
{
    private readonly KartovaApiFixture _fx;

    public TenantScopeMechanismTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task SaveChanges_throws_from_interceptor_when_scope_inactive()
    {
        // The interceptor must reject SaveChanges before any SQL is sent when the
        // ambient ITenantScope reports IsActive == false. Catches "new endpoint
        // added without RequireTenantScope()" and similar wiring mistakes.
        var inactiveScope = new InactiveScopeStub();
        var interceptor = new TenantScopeRequiredInterceptor(inactiveScope);

        await using var conn = new NpgsqlConnection(_fx.MainConnectionString);
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(conn)
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new OrganizationDbContext(options);
        db.Add(Kartova.Organization.Domain.Organization.Create("never-saved"));

        var act = async () => await db.SaveChangesAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*ITenantScope*");
    }

    [Fact]
    public async Task Commit_failure_after_write_propagates_and_persists_no_data()
    {
        // Durability promise of ADR-0090: if CommitAsync fails after a successful
        // INSERT inside the scope's transaction, the failure is observable to the
        // caller (HTTP middleware turns it into 5xx) and no row survives.
        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        var npgScope = (INpgsqlTenantScope)tenantScope;
        var rawScope = (TenantScope)tenantScope;

        var rowName = $"CommitFail-{Guid.NewGuid()}";
        var rowId = Guid.NewGuid();

        var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);
        try
        {
            var tx = TransactionViaReflection(rawScope);

            await using (var insertCmd = npgScope.Connection.CreateCommand())
            {
                insertCmd.Transaction = tx;
                insertCmd.CommandText =
                    "INSERT INTO organizations (id, tenant_id, name, created_at) " +
                    "VALUES ($1, $2, $3, now())";
                insertCmd.Parameters.AddWithValue(rowId);
                insertCmd.Parameters.AddWithValue(SeededOrgs.OrgA.Value);
                insertCmd.Parameters.AddWithValue(rowName);
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Force commit failure: close the underlying connection while the tx is open.
            await npgScope.Connection.CloseAsync();

            var commit = async () => await handle.CommitAsync(default);
            await commit.Should().ThrowAsync<Exception>(
                because: "commit on a closed connection must surface as an exception");
        }
        finally
        {
            await handle.DisposeAsync();
        }

        await AssertNoOrganizationWithName(rowName,
            because: "commit failed, so the INSERT must not be visible");
    }

    [Fact]
    public async Task Exception_during_handler_rolls_back_uncommitted_writes()
    {
        // Handler exits without CommitAsync; DisposeAsyncCore must roll back any
        // uncommitted writes so partial state never reaches the table.
        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        var npgScope = (INpgsqlTenantScope)tenantScope;
        var rawScope = (TenantScope)tenantScope;

        var rowName = $"Rollback-{Guid.NewGuid()}";
        var rowId = Guid.NewGuid();

        await using (var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default))
        {
            var tx = TransactionViaReflection(rawScope);

            await using var insertCmd = npgScope.Connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO organizations (id, tenant_id, name, created_at) " +
                "VALUES ($1, $2, $3, now())";
            insertCmd.Parameters.AddWithValue(rowId);
            insertCmd.Parameters.AddWithValue(SeededOrgs.OrgA.Value);
            insertCmd.Parameters.AddWithValue(rowName);
            await insertCmd.ExecuteNonQueryAsync();

            // Exit without CommitAsync — simulates a thrown handler.
        }

        await AssertNoOrganizationWithName(rowName,
            because: "scope disposed without commit must roll back the INSERT");
    }

    private async Task AssertNoOrganizationWithName(string name, string because)
    {
        await using var bypass = new NpgsqlConnection(_fx.BypassConnectionString);
        await bypass.OpenAsync();
        await using var cmd = bypass.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM organizations WHERE name = $1";
        cmd.Parameters.AddWithValue(name);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(0, because: because);
    }

    private static NpgsqlTransaction TransactionViaReflection(TenantScope scope)
    {
        // TenantScope.Transaction is internal; tests reach in to share the per-request
        // transaction so direct-SQL writes participate in the same atomic unit as the
        // DbContext would. No production code touches this property.
        var prop = typeof(TenantScope).GetProperty("Transaction",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TenantScope.Transaction property not found.");
        return (NpgsqlTransaction)prop.GetValue(scope)!;
    }

    private sealed class InactiveScopeStub : ITenantScope
    {
        public bool IsActive => false;
        public Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct) =>
            throw new NotSupportedException("Stub — not used by the interceptor under test.");
    }
}
