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

        // Pagination requires a non-trivial fixture to be exercisable in `docker compose up`
        // (ADR-0095 §10). Seed ~120 applications for Org A with deterministic varied names so
        // sort-by-name and sort-by-createdAt produce visibly different orderings.
        await ExecAsync(conn, "ALTER TABLE catalog_applications NO FORCE ROW LEVEL SECURITY;");
        try
        {
            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM catalog_applications WHERE tenant_id = $1;";
            checkCmd.Parameters.AddWithValue(OrgATenantId);
            var existing = (long?)await checkCmd.ExecuteScalarAsync() ?? 0L;

            if (existing == 0L)
            {
                // Names chosen so alphabetical and chronological orders diverge.
                var origin = DateTimeOffset.UtcNow.AddMinutes(-120);
                for (var i = 0; i < 120; i++)
                {
                    await using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = """
                        INSERT INTO catalog_applications (id, tenant_id, name, display_name, description, owner_user_id, created_at)
                        VALUES (gen_random_uuid(), $1, $2, $3, $4, gen_random_uuid(), $5);
                        """;
                    // Reverse-alphabetical name relative to insertion order so name-asc != createdAt-asc.
                    var letter = (char)('a' + ((119 - i) % 26));
                    var name = $"{letter}-app-{i:D3}";
                    var displayName = char.ToUpper(letter) + $" App {i:D3}";
                    insertCmd.Parameters.AddWithValue(OrgATenantId);
                    insertCmd.Parameters.AddWithValue(name);
                    insertCmd.Parameters.AddWithValue(displayName);
                    insertCmd.Parameters.AddWithValue($"Seeded application #{i + 1}");
                    insertCmd.Parameters.AddWithValue(origin.AddMinutes(i));
                    await insertCmd.ExecuteNonQueryAsync();
                }
                logger.LogInformation("Dev seed: inserted 120 applications for Org A.");
            }
            else
            {
                logger.LogInformation("Dev seed: applications already present (Count={Count}).", existing);
            }
        }
        finally
        {
            await ExecAsync(conn, "ALTER TABLE catalog_applications FORCE ROW LEVEL SECURITY;");
        }
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
