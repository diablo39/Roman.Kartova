using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// Proves the DB-enforced insert-only constraint and per-tenant RLS on audit_log.
///
/// Each test opens a fresh <c>kartova_app</c> connection with the tenant GUC set,
/// mirroring exactly how <see cref="Kartova.SharedKernel.Postgres.TenantScope"/>
/// opens the request-scoped connection in production (ADR-0090).
///
/// The UPDATE / DELETE / TRUNCATE tests assert SQLSTATE 42501 (insufficient_privilege)
/// which proves the <c>REVOKE</c> SQL in the migration took effect.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class AuditLogGrantsAndRlsTests
{
    private static AuditLogFixture Fx => IntegrationTestAssemblySetup.Fx;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// Opens an app-role connection with the tenant GUC set to <paramref name="tenantId"/>,
    /// mirroring what TenantScope does at request start via <c>set_config(..., true)</c>
    /// (transaction-local). These raw tests use <c>false</c> (session-scoped) because they
    /// run outside a transaction, but the RLS policy evaluates <c>current_setting</c>
    /// identically in both modes.
    /// </summary>
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

    /// <summary>
    /// Inserts a minimal audit_log row via <paramref name="conn"/>. The connection must
    /// already have the tenant GUC set so the RLS WITH CHECK passes.
    /// </summary>
    private static async Task InsertRowAsync(NpgsqlConnection conn, Guid tenantId, long seq)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO audit_log (id, tenant_id, seq, occurred_at, actor_type, actor_id, actor_display,
                       action, target_type, target_id, data, prev_hash, row_hash)
VALUES ($1, $2, $3, now(), 'User', $4, NULL, 'test.action', 'User', $5, NULL, $6, $7)";
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(seq);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue(new byte[32]);
        cmd.Parameters.AddWithValue(new byte[32]);
        await cmd.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task App_role_can_insert_into_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertRowAsync(conn, TenantA, seq: 1000);
        // No exception == INSERT privilege is intact.
    }

    [TestMethod]
    public async Task App_role_cannot_update_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertRowAsync(conn, TenantA, seq: 1001);

        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE audit_log SET action = 'tampered' WHERE tenant_id = $1";
        upd.Parameters.AddWithValue(TenantA);

        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => upd.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501 (insufficient_privilege), got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task App_role_cannot_delete_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertRowAsync(conn, TenantA, seq: 1002);

        await using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM audit_log WHERE tenant_id = $1";
        del.Parameters.AddWithValue(TenantA);

        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => del.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501 (insufficient_privilege), got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task App_role_cannot_truncate_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await using var tr = conn.CreateCommand();
        tr.CommandText = "TRUNCATE audit_log";

        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => tr.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501 (insufficient_privilege), got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task Bypass_role_cannot_update_audit_log()
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE audit_log SET action = 'tampered'";
        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501 (insufficient_privilege), got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task Bypass_role_cannot_delete_audit_log()
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_log";
        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState, $"Expected 42501 (insufficient_privilege), got: {ex.SqlState}");
    }

    [TestMethod]
    public async Task Rls_hides_other_tenants_rows()
    {
        // Insert as tenant A (unique seq to avoid collision with other tests).
        await using (var a = await OpenAppScopedAsync(TenantA))
        {
            await InsertRowAsync(a, TenantA, seq: 2000);
        }

        // Open as tenant B and verify seq 2000 from tenant A is invisible.
        await using var b = await OpenAppScopedAsync(TenantB);
        await using var q = b.CreateCommand();
        q.CommandText = "SELECT count(*) FROM audit_log WHERE seq = 2000";
        var count = (long)(await q.ExecuteScalarAsync())!;
        Assert.AreEqual(0, count, "Tenant B must not see Tenant A's row (RLS not working)");
    }
}
