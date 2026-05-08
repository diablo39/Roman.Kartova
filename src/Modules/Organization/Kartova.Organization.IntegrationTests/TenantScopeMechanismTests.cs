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
        db.Add(Kartova.Organization.Domain.Organization.Create("never-saved", TimeProvider.System));

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

        var rowName = $"CommitFail-{Guid.NewGuid()}";
        var rowId = Guid.NewGuid();

        var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);
        try
        {
            var db = sp.GetRequiredService<OrganizationDbContext>();
            var org = OrganizationTestHelper.CreateWithTenant(rowId, SeededOrgs.OrgA, rowName);
            db.Add(org);
            await db.SaveChangesAsync(default);

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

        var rowName = $"Rollback-{Guid.NewGuid()}";
        var rowId = Guid.NewGuid();

        await using (var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default))
        {
            var db = sp.GetRequiredService<OrganizationDbContext>();
            var org = OrganizationTestHelper.CreateWithTenant(rowId, SeededOrgs.OrgA, rowName);
            db.Add(org);
            await db.SaveChangesAsync(default);

            // Exit without CommitAsync — simulates a thrown handler.
        }

        await AssertNoOrganizationWithName(rowName,
            because: "scope disposed without commit must roll back the INSERT");
    }

    [Fact]
    public async Task BeginAsync_throws_when_called_twice_on_the_same_scope()
    {
        // Slice-3 §13.11: pin the "already begun" guard. The transport adapter is the
        // only legitimate caller of BeginAsync; a second call indicates a wiring bug
        // and must surface immediately rather than silently overwriting the connection.
        using var hostScope = _fx.Services.CreateScope();
        var tenantScope = hostScope.ServiceProvider.GetRequiredService<ITenantScope>();

        var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);
        try
        {
            var second = async () => await tenantScope.BeginAsync(SeededOrgs.OrgA, default);
            await second.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*already begun*");
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }

    [Fact]
    public async Task Handle_DisposeAsync_is_idempotent()
    {
        // Slice-3 §13.11: pin Handle.DisposeAsync's _disposed guard. Idempotent dispose
        // is required because the transport adapter's `await using` plus the begin-middleware's
        // `finally` block may both reach DisposeAsync — double-dispose should be safe.
        using var hostScope = _fx.Services.CreateScope();
        var tenantScope = hostScope.ServiceProvider.GetRequiredService<ITenantScope>();

        var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);
        await handle.DisposeAsync();

        // Second dispose must not throw.
        var act = async () => await handle.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CommitAsync_throws_when_scope_was_disposed_first()
    {
        // Slice-3 §13.11: pin the CommitAsync transaction-null guard. Once the scope is
        // disposed (rollback path), calling CommitAsync via the same Handle must surface
        // a programmer error rather than silently no-op (which would mask a wiring bug).
        using var hostScope = _fx.Services.CreateScope();
        var tenantScope = hostScope.ServiceProvider.GetRequiredService<ITenantScope>();

        var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);
        await handle.DisposeAsync();

        var commit = async () => await handle.CommitAsync(default);
        await commit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot commit*scope not active*");
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

    private sealed class InactiveScopeStub : ITenantScope
    {
        public bool IsActive => false;
        public Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct) =>
            throw new NotSupportedException("Stub — not used by the interceptor under test.");
    }
}
