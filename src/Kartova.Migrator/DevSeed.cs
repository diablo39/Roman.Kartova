using Kartova.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Kartova.Migrator;

internal static class DevSeed
{
    // Org A tenant id is mirrored from deploy/keycloak/realm-kartova.json (admin@orga's
    // tenant_id claim). Duplicated by tests/Kartova.Testing.Auth/SeededOrgs.cs; both sides
    // reference the realm seed as the authoritative source rather than a runtime constant —
    // this is dev-fixture data with no production meaning.
    private static readonly Guid OrgATenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task RunAsync(IConfiguration config, ILogger logger)
    {
        var connection = KartovaConnectionStrings.RequireMain(config);
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();

        // The migrator role owns `organizations` but lacks BYPASSRLS; FORCE applies
        // the policy to the owner too. Toggle off → seed → toggle on. try/finally
        // restores FORCE even if the INSERT fails.
        await ExecAsync(conn, "ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;");
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO organizations (id, tenant_id, name, created_at)
                VALUES ($1, $2, $3, now())
                ON CONFLICT (id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue(OrgATenantId);
            cmd.Parameters.AddWithValue(OrgATenantId);
            cmd.Parameters.AddWithValue("Org A");
            var rows = await cmd.ExecuteNonQueryAsync();
            logger.LogInformation("Dev seed: Org A {Result}.", rows == 1 ? "inserted" : "already present");
        }
        finally
        {
            await ExecAsync(conn, "ALTER TABLE organizations FORCE ROW LEVEL SECURITY;");
        }
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
