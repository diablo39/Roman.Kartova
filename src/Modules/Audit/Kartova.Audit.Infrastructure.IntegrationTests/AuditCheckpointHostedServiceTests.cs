using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kartova.Audit.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// End-to-end test for <see cref="AuditCheckpointHostedService"/> (ADR-0105 cadence): the sweep
/// enumerates tenants whose chain head has advanced past their latest checkpoint (via the BYPASSRLS
/// <see cref="AdminAuditDbContext"/>) and checkpoints each one through the tenant-scoped path. The
/// base-class timer + advisory lock are bypassed by calling the public work method directly.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class AuditCheckpointHostedServiceTests
{
    private static AuditLogFixture Fx => IntegrationTestAssemblySetup.Fx;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(Fx.AppConnectionString);
        services.AddTenantScope();
        services.AddModuleDbContext<AuditDbContext>();
        services.AddDbContext<AdminAuditDbContext>(opts => opts.UseNpgsql(Fx.BypassConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditWriter>();
        services.AddScoped<AuditCheckpointer>();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.TeamMemberships.Returns(Array.Empty<TeamMembershipInfo>());
        user.TeamIds.Returns(new HashSet<Guid>());
        services.AddScoped(_ => user);

        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task AppendAsync(ServiceProvider sp, TenantId tenant, int count)
    {
        using var scope = sp.CreateScope();
        var tenantContext = (TenantContextAccessor)scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.Populate(tenant, Array.Empty<string>());
        await using var handle = await scope.ServiceProvider.GetRequiredService<ITenantScope>().BeginAsync(tenant, CancellationToken.None);
        var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        for (var i = 0; i < count; i++)
        {
            await writer.AppendAsync(
                new AuditEntry("member.role_changed", "User", Guid.NewGuid().ToString(),
                    new Dictionary<string, string?> { ["k"] = "v" }),
                CancellationToken.None);
        }
        await handle.CommitAsync(CancellationToken.None);
    }

    private static AuditCheckpointHostedService NewService(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        Substitute.For<IDistributedLock>(), // unused by the work method
        TimeProvider.System,
        NullLogger<AuditCheckpointHostedService>.Instance);

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

    [TestMethod]
    public async Task Sweep_checkpoints_every_tenant_with_a_chain_at_its_head()
    {
        var tenantA = new TenantId(Guid.NewGuid());
        var tenantB = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();

        await AppendAsync(sp, tenantA, 3);
        await AppendAsync(sp, tenantB, 2);

        await NewService(sp).CheckpointAllTenantsAsync(sp, CancellationToken.None);

        Assert.AreEqual(3L, await LatestCheckpointSeqAsync(tenantA.Value), "tenant A should be checkpointed at its head (seq 3)");
        Assert.AreEqual(2L, await LatestCheckpointSeqAsync(tenantB.Value), "tenant B should be checkpointed at its head (seq 2)");
    }

    [TestMethod]
    public async Task Sweep_isolates_a_broken_chain_tenant_and_still_checkpoints_the_healthy_one()
    {
        var healthy = new TenantId(Guid.NewGuid());
        var broken = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();

        await AppendAsync(sp, healthy, 3);
        // Forge a broken genesis row for the other tenant (bypass role; app code can't).
        await InsertForgedRowAsync(broken.Value, seq: 1);

        await NewService(sp).CheckpointAllTenantsAsync(sp, CancellationToken.None);

        Assert.AreEqual(3L, await LatestCheckpointSeqAsync(healthy.Value), "the healthy tenant must still be checkpointed despite the broken one");
        Assert.IsNull(await LatestCheckpointSeqAsync(broken.Value), "a broken chain must never be checkpointed");
    }

    private static async Task InsertForgedRowAsync(Guid tenantId, long seq)
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
        cmd.Parameters.AddWithValue(new byte[32]); // prev_hash = genesis
        cmd.Parameters.AddWithValue(new byte[32]); // row_hash = bogus (won't recompute)
        await cmd.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task Sweep_advances_existing_checkpoints_and_skips_tenants_with_no_new_rows()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider();

        await AppendAsync(sp, tenant, 2);
        await NewService(sp).CheckpointAllTenantsAsync(sp, CancellationToken.None);
        Assert.AreEqual(2L, await LatestCheckpointSeqAsync(tenant.Value));

        // Second sweep with no new rows is a no-op (UpToDate) — checkpoint stays at seq 2.
        await NewService(sp).CheckpointAllTenantsAsync(sp, CancellationToken.None);
        Assert.AreEqual(2L, await LatestCheckpointSeqAsync(tenant.Value));

        // After more appends the next sweep advances the checkpoint to the new head.
        await AppendAsync(sp, tenant, 3);
        await NewService(sp).CheckpointAllTenantsAsync(sp, CancellationToken.None);
        Assert.AreEqual(5L, await LatestCheckpointSeqAsync(tenant.Value));
    }
}
