using Kartova.SharedKernel;
using Kartova.SharedKernel.Multitenancy;
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

    // Fixed Keycloak user id for team-admin@orga.kartova.local (ADR-0101: realm Member who is
    // team Admin). Mirrors the "id" field added to that user in kartova-realm.json so the
    // team_members FK aligns with Keycloak's imported user id.
    // Keycloak convention: the imported user `id` becomes the runtime JWT `sub` claim →
    // ICurrentUser.UserId → the TeamMemberships lookup that the TeamAdminOfThis resource gate
    // uses. That's why this GUID MUST stay in sync between kartova-realm.json and this file —
    // if they drift, the seeded Admin membership won't match the authenticated user's sub.
    private static readonly Guid TeamAdminUserId = Guid.Parse("aaaabbbb-0001-0001-0001-000000000001");

    // Fixed id for the demo team seeded for team-admin@orga.kartova.local. Pinned so docs/evidence
    // curl examples and re-seeds reference a stable team id (and ON CONFLICT (id) DO NOTHING makes
    // re-seeding idempotent).
    private static readonly Guid DemoTeamId = Guid.Parse("dddddddd-0001-0001-0001-000000000001");

    public static async Task RunAsync(IConfiguration config, ILogger logger)
    {
        var connection = KartovaConnectionStrings.RequireMain(config);
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();

        // The migrator role owns `organizations` but lacks BYPASSRLS; FORCE applies
        // the policy to the owner too. Toggle off → seed → toggle on. try/finally
        // restores FORCE even if the INSERT fails.
        try
        {
            await ExecAsync(conn, "ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;");
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

        // Seed the users row for team-admin@orga so the realm_role write-through cache
        // (ADR-0102) is populated for the docker-compose smoke scenario. Only this user
        // has a pinned KC id in kartova-realm.json; admin@orga / member@orga get their
        // users rows on first login via the session-bootstrap hook (their KC sub is not
        // fixed in the realm import). realm_role = 'Member' per ADR-0101 (team-admin is
        // a realm Member who holds Admin TeamRole).
        try
        {
            await ExecAsync(conn, "ALTER TABLE users NO FORCE ROW LEVEL SECURITY;");
            await using var userCmd = conn.CreateCommand();
            userCmd.CommandText = """
                INSERT INTO users (id, tenant_id, email, display_name, realm_role, created_at)
                VALUES ($1, $2, $3, $4, $5, now())
                ON CONFLICT (id) DO UPDATE
                    SET realm_role = EXCLUDED.realm_role;
                """;
            userCmd.Parameters.AddWithValue(TeamAdminUserId);
            userCmd.Parameters.AddWithValue(OrgATenantId);
            userCmd.Parameters.AddWithValue("team-admin@orga.kartova.local");
            userCmd.Parameters.AddWithValue("Team Admin");
            userCmd.Parameters.AddWithValue("Member");
            var userRows = await userCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Dev seed: team-admin@orga users row {Result}.", userRows == 1 ? "inserted" : "updated");
        }
        finally
        {
            await ExecAsync(conn, "ALTER TABLE users FORCE ROW LEVEL SECURITY;");
        }

        // ADR-0101: team-admin@orga is now a realm-Member who holds the Admin TeamRole on a
        // demo team. Seed the demo team + Admin membership so docker-compose up demonstrates
        // the membership-authority model end-to-end.
        try
        {
            await ExecAsync(conn, "ALTER TABLE teams NO FORCE ROW LEVEL SECURITY;");
            await ExecAsync(conn, "ALTER TABLE team_members NO FORCE ROW LEVEL SECURITY;");
            await using var teamCmd = conn.CreateCommand();
            teamCmd.CommandText = """
                INSERT INTO teams (id, tenant_id, display_name, description, created_at)
                VALUES ($1, $2, $3, $4, now())
                ON CONFLICT (id) DO NOTHING;
                """;
            teamCmd.Parameters.AddWithValue(DemoTeamId);
            teamCmd.Parameters.AddWithValue(OrgATenantId);
            teamCmd.Parameters.AddWithValue("Demo Team");
            teamCmd.Parameters.AddWithValue("Seeded demo team for team-admin@orga (ADR-0101).");
            var teamRows = await teamCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Dev seed: demo team {Result}.", teamRows == 1 ? "inserted" : "already present");

            await using var memberCmd = conn.CreateCommand();
            memberCmd.CommandText = """
                INSERT INTO team_members (team_id, user_id, role, added_at)
                VALUES ($1, $2, $3, now())
                ON CONFLICT (team_id, user_id) DO NOTHING;
                """;
            memberCmd.Parameters.AddWithValue(DemoTeamId);
            memberCmd.Parameters.AddWithValue(TeamAdminUserId);
            memberCmd.Parameters.AddWithValue((byte)TeamRoleKind.Admin);
            var memberRows = await memberCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Dev seed: demo team Admin membership for team-admin@orga {Result}.",
                memberRows == 1 ? "inserted" : "already present");
        }
        finally
        {
            await ExecAsync(conn, "ALTER TABLE teams FORCE ROW LEVEL SECURITY;");
            await ExecAsync(conn, "ALTER TABLE team_members FORCE ROW LEVEL SECURITY;");
        }

        // Pagination requires a non-trivial fixture to be exercisable in `docker compose up`
        // (ADR-0095 §10). Seed ~120 applications for Org A with deterministic varied names so
        // sort-by-name and sort-by-createdAt produce visibly different orderings.
        // Seeded AFTER the demo team (above) because every app now requires a non-null
        // owning team (ADR-0103) — they are all owned by the demo team.
        try
        {
            await ExecAsync(conn, "ALTER TABLE catalog_applications NO FORCE ROW LEVEL SECURITY;");
            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM catalog_applications WHERE tenant_id = $1;";
            checkCmd.Parameters.AddWithValue(OrgATenantId);
            var existing = (long?)await checkCmd.ExecuteScalarAsync() ?? 0L;

            if (existing == 0L)
            {
                // Names chosen so alphabetical and chronological orders diverge.
                // ADR-0103: every app has a required owning team — the demo team
                // (seeded above) owns all 120; created_by_user_id stays a per-app
                // random id (creation provenance, immutable).
                var origin = DateTimeOffset.UtcNow.AddMinutes(-120);
                for (var i = 0; i < 120; i++)
                {
                    await using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = """
                        INSERT INTO catalog_applications (id, tenant_id, display_name, description, created_by_user_id, team_id, created_at)
                        VALUES (gen_random_uuid(), $1, $2, $3, gen_random_uuid(), $4, $5);
                        """;
                    // Varied display names so the docker-compose smoke renders aren't all-identical.
                    var letter = (char)('a' + ((119 - i) % 26));
                    var displayName = char.ToUpper(letter) + $" App {i:D3}";
                    insertCmd.Parameters.AddWithValue(OrgATenantId);
                    insertCmd.Parameters.AddWithValue(displayName);
                    insertCmd.Parameters.AddWithValue($"Seeded application #{i + 1}");
                    insertCmd.Parameters.AddWithValue(DemoTeamId);
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
