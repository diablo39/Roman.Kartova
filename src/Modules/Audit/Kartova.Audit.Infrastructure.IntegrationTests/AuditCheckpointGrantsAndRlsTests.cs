using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// Proves the DB-enforced insert-only constraint and per-tenant RLS on audit_checkpoint (ADR-0105):
/// a checkpoint inherits the exact trust model of the chain it attests to. Mirrors
/// <see cref="AuditLogGrantsAndRlsTests"/>. SQLSTATE 42501 (insufficient_privilege) proves the
/// REVOKE SQL in the migration took effect.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class AuditCheckpointGrantsAndRlsTests
{
    private static AuditLogFixture Fx => IntegrationTestAssemblySetup.Fx;

    private static readonly Guid TenantA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TenantB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static async Task<NpgsqlConnection> OpenAppScopedAsync(Guid tenantId)
    {
        var conn = new NpgsqlConnection(Fx.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', $1, false)";
        cmd.Parameters.AddWithValue(tenantId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task InsertCheckpointAsync(NpgsqlConnection conn, Guid tenantId, long seq)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO audit_checkpoint (id, tenant_id, seq, row_hash, created_at)
VALUES ($1, $2, $3, $4, now())";
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(seq);
        cmd.Parameters.AddWithValue(new byte[32]);
        await cmd.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task App_role_can_insert_into_audit_checkpoint()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertCheckpointAsync(conn, TenantA, seq: 1000);
        // No exception == INSERT privilege intact.
    }

    [TestMethod]
    public async Task App_role_cannot_update_audit_checkpoint()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertCheckpointAsync(conn, TenantA, seq: 1001);

        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE audit_checkpoint SET seq = 0 WHERE tenant_id = $1";
        upd.Parameters.AddWithValue(TenantA);

        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => upd.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501, got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task App_role_cannot_delete_audit_checkpoint()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertCheckpointAsync(conn, TenantA, seq: 1002);

        await using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM audit_checkpoint WHERE tenant_id = $1";
        del.Parameters.AddWithValue(TenantA);

        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => del.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501, got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task App_role_cannot_truncate_audit_checkpoint()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await using var tr = conn.CreateCommand();
        tr.CommandText = "TRUNCATE audit_checkpoint";

        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => tr.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501, got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task Bypass_role_cannot_delete_audit_checkpoint()
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_checkpoint";
        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501, got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task Rls_hides_other_tenants_checkpoints()
    {
        await using (var a = await OpenAppScopedAsync(TenantA))
        {
            await InsertCheckpointAsync(a, TenantA, seq: 2000);
        }

        await using var b = await OpenAppScopedAsync(TenantB);
        await using var q = b.CreateCommand();
        q.CommandText = "SELECT count(*) FROM audit_checkpoint WHERE seq = 2000";
        var count = (long)(await q.ExecuteScalarAsync())!;
        Assert.AreEqual(0, count, "Tenant B must not see Tenant A's checkpoint (RLS)");
    }
}
