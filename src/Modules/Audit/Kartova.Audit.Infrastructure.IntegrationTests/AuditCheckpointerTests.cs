using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kartova.Audit.Domain;
using Kartova.Audit.Infrastructure;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// End-to-end tests for <see cref="AuditCheckpointer"/> and the verify-from-checkpoint fast path on
/// <see cref="AuditChainVerifier"/> (ADR-0105), against a real Postgres instance. Wiring mirrors the
/// production composition root (NpgsqlDataSource + AddTenantScope + AddModuleDbContext), exactly as
/// <see cref="AuditWriterTests"/>.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class AuditCheckpointerTests
{
    private static AuditLogFixture Fx => IntegrationTestAssemblySetup.Fx;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(Fx.AppConnectionString);
        services.AddTenantScope();
        services.AddModuleDbContext<AuditDbContext>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditWriter>();
        services.AddScoped<AuditChainVerifier>();
        services.AddScoped<AuditCheckpointer>();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.TeamMemberships.Returns(Array.Empty<TeamMembershipInfo>());
        user.TeamIds.Returns(new HashSet<Guid>());
        services.AddScoped(_ => user);

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task<IAsyncTenantScopeHandle> BeginScopeAsync(IServiceProvider sp, TenantId tenant)
    {
        var tenantContext = (TenantContextAccessor)sp.GetRequiredService<ITenantContext>();
        tenantContext.Populate(tenant, Array.Empty<string>());
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        return await tenantScope.BeginAsync(tenant, CancellationToken.None);
    }

    private static AuditEntry SampleEntry() => new(
        Action: "member.role_changed",
        TargetType: "User",
        TargetId: Guid.NewGuid().ToString(),
        Data: new Dictionary<string, string?> { ["k"] = "v" });

    private static async Task AppendAsync(ServiceProvider sp, TenantId tenant, int count)
    {
        using var scope = sp.CreateScope();
        await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
        var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        for (var i = 0; i < count; i++)
            await writer.AppendAsync(SampleEntry(), CancellationToken.None);
        await handle.CommitAsync(CancellationToken.None);
    }

    // Creates a checkpoint and COMMITS the tenant scope so it persists across scopes (ADR-0090: the
    // scope owns the transaction; the checkpointer's SaveChanges only flushes within it).
    private static async Task<AuditCheckpointResult> CheckpointAsync(ServiceProvider sp, TenantId tenant)
    {
        using var scope = sp.CreateScope();
        await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
        var result = await scope.ServiceProvider.GetRequiredService<AuditCheckpointer>().CreateAsync(tenant, CancellationToken.None);
        await handle.CommitAsync(CancellationToken.None);
        return result;
    }

    [TestMethod]
    public async Task Create_over_an_intact_chain_writes_a_checkpoint_at_the_head()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 3);

        var result = await CheckpointAsync(sp, tenant);

        Assert.AreEqual(AuditCheckpointOutcome.Created, result.Outcome);
        Assert.IsNotNull(result.Checkpoint);
        Assert.AreEqual(3L, result.Checkpoint!.Seq, "checkpoint should attest to the current head seq");
    }

    [TestMethod]
    public async Task Create_is_up_to_date_when_no_rows_were_appended_since_the_last_checkpoint()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 2);

        // First checkpoint at seq 2.
        var first = await CheckpointAsync(sp, tenant);
        Assert.AreEqual(AuditCheckpointOutcome.Created, first.Outcome);

        // No new appends → second attempt is a no-op.
        var second = await CheckpointAsync(sp, tenant);
        Assert.AreEqual(AuditCheckpointOutcome.UpToDate, second.Outcome);
        Assert.AreEqual(2L, second.Checkpoint!.Seq);
    }

    [TestMethod]
    public async Task Create_advances_the_checkpoint_after_more_appends()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 2);

        await CheckpointAsync(sp, tenant); // seq 2

        await AppendAsync(sp, tenant, 3); // now 5 rows total

        var result = await CheckpointAsync(sp, tenant);
        Assert.AreEqual(AuditCheckpointOutcome.Created, result.Outcome);
        Assert.AreEqual(5L, result.Checkpoint!.Seq, "checkpoint should advance to the new head");
    }

    [TestMethod]
    public async Task Verify_from_checkpoint_reports_intact_on_a_good_tail()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 3);

        await CheckpointAsync(sp, tenant);

        await AppendAsync(sp, tenant, 2); // tail of 2 rows after the checkpoint

        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyFromCheckpointAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact, $"tail broken: seq={result.FirstBrokenSeq} reason={result.Reason}");
        }
    }

    [TestMethod]
    public async Task Verify_from_checkpoint_falls_back_to_full_walk_when_no_checkpoint_exists()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 3);

        using var scope = sp.CreateScope();
        await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
        var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
        var result = await verifier.VerifyFromCheckpointAsync(tenant, CancellationToken.None);
        Assert.IsTrue(result.Intact, $"chain broken: seq={result.FirstBrokenSeq} reason={result.Reason}");
    }

    [TestMethod]
    public async Task Verify_from_checkpoint_rejects_a_checkpoint_whose_attested_row_is_missing()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 2);

        await CheckpointAsync(sp, tenant);

        // Forge a checkpoint at seq 99 where no live row exists (bypass role is the only one that can
        // INSERT a checkpoint directly). The verifier's head re-check must reject it as missing.
        await InsertCheckpointAsync(tenant.Value, seq: 99, rowHash: new byte[32]);

        using var scope = sp.CreateScope();
        await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
        var result = await scope.ServiceProvider.GetRequiredService<AuditChainVerifier>()
            .VerifyFromCheckpointAsync(tenant, CancellationToken.None);

        Assert.IsFalse(result.Intact);
        Assert.AreEqual(99L, result.FirstBrokenSeq);
        Assert.AreEqual("checkpointed row is missing", result.Reason);
    }

    [TestMethod]
    public async Task Verify_from_checkpoint_rejects_a_checkpoint_whose_hash_differs_from_the_live_row()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 2);

        // Forge a checkpoint at seq 2 (a row that DOES exist) but with a wrong hash → the head
        // re-check must reject it as a mismatch, distinct from the missing-row branch.
        await InsertCheckpointAsync(tenant.Value, seq: 2, rowHash: new byte[32]);

        using var scope = sp.CreateScope();
        await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
        var result = await scope.ServiceProvider.GetRequiredService<AuditChainVerifier>()
            .VerifyFromCheckpointAsync(tenant, CancellationToken.None);

        Assert.IsFalse(result.Intact);
        Assert.AreEqual(2L, result.FirstBrokenSeq);
        Assert.AreEqual("checkpointed row_hash does not match live row", result.Reason);
    }

    [TestMethod]
    public async Task Verify_from_checkpoint_detects_a_tampered_tail_row_after_a_valid_checkpoint()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 3);
        await CheckpointAsync(sp, tenant); // valid checkpoint at seq 3

        // A forged row 4 in the tail (wrong prev_hash) — the head re-check at seq 3 passes, so this
        // exercises the seeded tail walk, which must catch the break.
        await InsertForgedRowAsync(tenant.Value, seq: 4, prevHash: new byte[32], rowHash: new byte[32]);

        using var scope = sp.CreateScope();
        await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
        var result = await scope.ServiceProvider.GetRequiredService<AuditChainVerifier>()
            .VerifyFromCheckpointAsync(tenant, CancellationToken.None);

        Assert.IsFalse(result.Intact, "a tampered tail row beyond the checkpoint must be detected");
        Assert.AreEqual(4L, result.FirstBrokenSeq);
    }

    [TestMethod]
    public async Task Create_over_a_broken_chain_returns_ChainBroken_and_writes_no_checkpoint()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();

        // No app appends — forge a single genesis row whose row_hash does not recompute.
        await InsertForgedRowAsync(tenant.Value, seq: 1, prevHash: new byte[32], rowHash: new byte[32]);

        var result = await CheckpointAsync(sp, tenant);

        Assert.AreEqual(AuditCheckpointOutcome.ChainBroken, result.Outcome);
        Assert.IsNull(result.Checkpoint);
        Assert.IsFalse(result.Verification.Intact);
        Assert.IsNull(await LatestCheckpointSeqAsync(tenant.Value), "a broken chain must never be checkpointed");
    }

    [TestMethod]
    public async Task Create_with_a_broken_tail_preserves_the_existing_checkpoint()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();
        await AppendAsync(sp, tenant, 3);
        await CheckpointAsync(sp, tenant); // checkpoint at seq 3

        // Forge a broken row 4 in the tail.
        await InsertForgedRowAsync(tenant.Value, seq: 4, prevHash: new byte[32], rowHash: new byte[32]);

        var result = await CheckpointAsync(sp, tenant);

        Assert.AreEqual(AuditCheckpointOutcome.ChainBroken, result.Outcome);
        Assert.AreEqual(4L, result.Verification.FirstBrokenSeq);
        Assert.AreEqual(3L, await LatestCheckpointSeqAsync(tenant.Value), "the prior checkpoint must not advance over a broken tail");
    }

    [TestMethod]
    public async Task Create_on_an_empty_chain_is_up_to_date_with_no_checkpoint()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();

        var result = await CheckpointAsync(sp, tenant);

        Assert.AreEqual(AuditCheckpointOutcome.UpToDate, result.Outcome);
        Assert.IsNull(result.Checkpoint);
        Assert.IsNull(await LatestCheckpointSeqAsync(tenant.Value));
    }

    private static async Task<long?> LatestCheckpointSeqAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT max(seq) FROM audit_checkpoint WHERE tenant_id = $1";
        cmd.Parameters.AddWithValue(tenantId);
        var result = await cmd.ExecuteScalarAsync();
        return result is long s ? s : null;
    }

    // Forge an audit_log row directly via the bypass role (app code can't, by design) to simulate
    // storage-layer tampering: a row whose prev_hash/row_hash do not chain correctly.
    private static async Task InsertForgedRowAsync(Guid tenantId, long seq, byte[] prevHash, byte[] rowHash)
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO audit_log (id, tenant_id, seq, occurred_at, actor_type, actor_id, actor_display,
                       action, target_type, target_id, data, prev_hash, row_hash)
VALUES (gen_random_uuid(), $1, $2, now(), 'User', gen_random_uuid(), NULL, 'x.forged', 'User', 'x',
        NULL, $3, $4)";
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(seq);
        cmd.Parameters.AddWithValue(prevHash);
        cmd.Parameters.AddWithValue(rowHash);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertCheckpointAsync(Guid tenantId, long seq, byte[] rowHash)
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO audit_checkpoint (id, tenant_id, seq, row_hash, created_at)
VALUES (gen_random_uuid(), $1, $2, $3, now())";
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(seq);
        cmd.Parameters.AddWithValue(rowHash);
        await cmd.ExecuteNonQueryAsync();
    }
}
