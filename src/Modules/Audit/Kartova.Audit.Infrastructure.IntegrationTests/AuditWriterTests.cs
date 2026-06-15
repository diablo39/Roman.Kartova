using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kartova.Audit.Infrastructure;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// End-to-end integration tests for <see cref="AuditWriter"/> and
/// <see cref="AuditChainVerifier"/> against a real Postgres instance.
///
/// Each test builds a minimal DI provider wired identically to the production
/// composition root (NpgsqlDataSource + AddTenantScope + AddModuleDbContext)
/// and drives the tenant scope directly, mirroring what the HTTP transport
/// adapter + request pipeline does (ADR-0090).
///
/// Key wiring note: <c>ITenantContext</c> must be populated (via
/// <see cref="TenantContextAccessor.Populate"/>) before <see cref="AuditWriter"/>
/// is called, exactly as <see cref="TenantClaimsTransformation"/> does in
/// production from the JWT claims. Without this, <c>tenant.Id.Value</c> returns
/// <c>TenantId.Empty</c> and the writer uses a nil GUID as the tenant.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class AuditWriterTests
{
    private static AuditLogFixture Fx => IntegrationTestAssemblySetup.Fx;

    /// <summary>
    /// Builds a minimal DI provider that mirrors the production request pipeline's
    /// audit-relevant wiring: NpgsqlDataSource (as kartova_app) + ITenantScope +
    /// AuditDbContext enlisted in the scope + stubbed ICurrentUser.
    /// </summary>
    private static ServiceProvider BuildProvider(Guid actorId)
    {
        var services = new ServiceCollection();

        // AddNpgsqlDataSource registers the Npgsql data source that TenantScope uses
        // to open its per-request connection as kartova_app.
        services.AddNpgsqlDataSource(Fx.AppConnectionString);

        // AddTenantScope registers: ITenantScope, INpgsqlTenantScope, ITenantContext
        // (as TenantContextAccessor), TenantScopeRequiredInterceptor,
        // EnlistInTenantScopeInterceptor. Must precede AddModuleDbContext.
        services.AddTenantScope();

        // AddModuleDbContext wires AuditDbContext to share the scope's connection + tx.
        services.AddModuleDbContext<AuditDbContext>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditWriter>();
        services.AddScoped<AuditChainVerifier>();

        // ICurrentUser stub — AuditWriter reads UserId to record the actor.
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(actorId);
        user.TeamMemberships.Returns(Array.Empty<TeamMembershipInfo>());
        user.TeamIds.Returns(new HashSet<Guid>());
        services.AddScoped(_ => user);

        // Microsoft.Extensions.Logging is needed by TenantScope (ILogger<TenantScope>).
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Begins a tenant scope AND populates ITenantContext, mirroring what the
    /// production transport layer does: the JWT claims transformation populates
    /// ITenantContext before the scope opens the connection (ADR-0090).
    /// </summary>
    private static async Task<IAsyncTenantScopeHandle> BeginScopeAsync(
        IServiceProvider sp, TenantId tenant, CancellationToken ct = default)
    {
        // Populate ITenantContext so AuditWriter.AppendAsync can read tenant.Id.Value.
        var tenantContext = (TenantContextAccessor)sp.GetRequiredService<ITenantContext>();
        tenantContext.Populate(tenant, Array.Empty<string>());

        var tenantScope = sp.GetRequiredService<ITenantScope>();
        return await tenantScope.BeginAsync(tenant, ct);
    }

    private static async Task<long> CountRowsAsync(string bypassConnectionString, Guid tenantId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(bypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM audit_log WHERE tenant_id = $1";
        cmd.Parameters.AddWithValue(tenantId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static AuditEntry SampleEntry(Guid target) => new(
        Action: "member.role_changed",
        TargetType: "User",
        TargetId: target.ToString(),
        Data: new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" });

    [TestMethod]
    public async Task Append_three_then_verify_intact_with_contiguous_seq()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var actor = Guid.NewGuid();
        await using var sp = BuildProvider(actor);

        // Write 3 entries in one transaction.
        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await handle.CommitAsync(CancellationToken.None);
        }

        // Verify the chain in a fresh scope.
        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact, $"Chain broken: seq={result.FirstBrokenSeq} reason={result.Reason}");
            Assert.AreEqual(3L, await CountRowsAsync(Fx.BypassConnectionString, tenant.Value),
                "three committed appends must persist exactly three rows");
        }
    }

    [TestMethod]
    public async Task Append_persists_jsonb_data_that_verifies_after_round_trip()
    {
        // Guards against false tamper alarms caused by Postgres jsonb normalization:
        // the canonical hash must be stable after the data dictionary is stored and
        // re-read (design spec §5). A successful Verify on the re-read rows proves it.
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider(Guid.NewGuid());

        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await handle.CommitAsync(CancellationToken.None);
        }

        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact, $"jsonb round-trip broke the chain: seq={result.FirstBrokenSeq} reason={result.Reason}");
        }
    }

    [TestMethod]
    public async Task Rolled_back_scope_persists_no_audit_row()
    {
        // The transactional fail-closed substrate: an append that is not committed
        // leaves no row. In Phase 2 a business write rides the same transaction, so
        // an audit failure rolls the business change back too (ADR-0018 / ADR-0090).
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider(Guid.NewGuid());

        // Append but dispose without CommitAsync — simulates a thrown handler.
        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            // handle.DisposeAsync() called by 'await using' without CommitAsync → rollback.
        }

        // Reopen and verify: chain must be intact AND empty (no rows survived the rollback).
        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact, $"Chain should be intact (empty): seq={result.FirstBrokenSeq} reason={result.Reason}");
            Assert.AreEqual(0L, await CountRowsAsync(Fx.BypassConnectionString, tenant.Value),
                "rolled-back append must leave zero rows");
        }
    }
}
