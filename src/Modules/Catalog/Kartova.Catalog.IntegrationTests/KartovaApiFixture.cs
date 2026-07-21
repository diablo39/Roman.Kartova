using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.Audit.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using DomainApplication = Kartova.Catalog.Domain.Application;
using DomainSystem = Kartova.Catalog.Domain.CatalogSystem;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Catalog-specific fixture. All cross-module plumbing (Postgres container,
/// role bootstrap, JWT signer wiring, env-var wiring of the Kartova.Api host,
/// JWT minting helpers) lives in <see cref="KartovaApiFixtureBase"/>; this
/// type only declares which DbContexts to migrate.
///
/// Slice 8 — migrates the Organization schema too: the Catalog module's
/// <c>AssignApplicationTeamHandler</c> resolves the cross-module
/// <c>IOrganizationTeamExistenceChecker</c>, which queries <c>teams</c>.
/// Without that table the PUT /applications/{id}/team endpoint would throw on
/// a missing relation rather than evaluate the 422 invalid-team branch.
/// </summary>
[ExcludeFromCodeCoverage]
public class KartovaApiFixture : KartovaApiFixtureBase
{
    protected override async Task RunModuleMigrationsAsync(string migratorConnectionString)
    {
        await PostgresTestBootstrap.RunMigrationsAsync<CatalogDbContext>(
            migratorConnectionString,
            opts => new CatalogDbContext(opts));
        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            migratorConnectionString,
            opts => new OrganizationDbContext(opts));
        // Audit event-wiring: AuditWiringTests assert rows in audit_log; the table must exist
        // before the test host starts. Mirrors the Organization fixture.
        await PostgresTestBootstrap.RunMigrationsAsync<AuditDbContext>(
            migratorConnectionString,
            opts => new AuditDbContext(opts));
    }

    /// <summary>
    /// Creates an HTTP client with a bearer token scoped to OrgA
    /// ("admin@orga.kartova.local"). Synchronous overload for tests that
    /// cannot await during field initialisation.
    /// </summary>
    public HttpClient CreateClientForOrgA() => CreateClientForEmail("admin@orga.kartova.local");

    /// <summary>
    /// Creates an HTTP client with a bearer token scoped to OrgB
    /// ("admin@orgb.kartova.local").
    /// </summary>
    public HttpClient CreateClientForOrgB() => CreateClientForEmail("admin@orgb.kartova.local");

    private HttpClient CreateClientForEmail(string email)
    {
        // Reuse the deterministic sub + tenant derivation from the base class
        // by issuing a token directly via the TestJwtSigner — mirrors what
        // CreateAuthenticatedClientAsync does but without the async wrapper.
        var tenant = TenantFor(email);
        var token = Signer.IssueForTenant(tenant, [KartovaRoles.OrgAdmin], subject: SubFor(email).ToString());
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Returns the deterministic <see cref="TenantId"/> for <paramref name="email"/>'s
    /// domain — the same value RLS uses. Exposed so test classes can seed rows for
    /// the correct tenant without re-implementing the derivation algorithm.
    /// </summary>
    public TenantId TenantIdForEmail(string email) => TenantFor(email);

    /// <summary>
    /// Seeds <paramref name="count"/> applications for the given tenant, with
    /// spread-apart <c>createdAt</c> timestamps so sort-by-createdAt tests are
    /// deterministic. Uses the bypass-RLS connection so rows can be inserted
    /// without an active tenant scope. <paramref name="namePrefix"/> drives the
    /// <c>displayName</c> column (ADR-0098: the kebab <c>name</c> column was dropped).
    /// </summary>
    public async Task SeedApplicationsAsync(TenantId tenantId, int count, string namePrefix)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        var origin = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            // ADR-0103: TeamId is required. These pagination/RLS fixtures never join to
            // a teams row, so a fresh id satisfies the non-null invariant (no FK exists).
            db.Applications.Add(DomainApplication.Create(
                displayName: $"{namePrefix}{i:D3}",
                description: "seeded for pagination tests",
                createdByUserId: Guid.NewGuid(),
                teamId: Guid.NewGuid(),
                tenantId: tenantId,
                createdAt: origin.AddMinutes(i)));
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a single application row, bypassing RLS so the delete is not
    /// blocked by the missing tenant scope.
    /// </summary>
    public async Task DeleteApplicationAsync(TenantId tenantId, Guid applicationId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM catalog_applications WHERE id = {0} AND tenant_id = {1}",
            applicationId, tenantId.Value);
    }

    /// <summary>
    /// Seeds <paramref name="count"/> applications in the given lifecycle state
    /// for the given tenant, with spread-apart <c>createdAt</c> timestamps.
    /// Slice 6 — used by ListApplicationsPaginationTests to populate Decommissioned
    /// rows that ADR-0073's default-view filter must hide.
    /// </summary>
    public async Task SeedApplicationsWithLifecycleAsync(
        TenantId tenantId,
        int count,
        string namePrefix,
        Lifecycle lifecycle)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        var origin = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            var app = DomainApplication.Create(
                displayName: $"{namePrefix}{i:D3}",
                description: "seeded for filter tests",
                createdByUserId: Guid.NewGuid(),
                teamId: Guid.NewGuid(),
                tenantId: tenantId,
                createdAt: origin.AddMinutes(i));

            // Drive the aggregate into the desired terminal state via its own methods,
            // not by reflection on the private setter — keeps the test honest about
            // what the production state machine actually does.
            if (lifecycle == Lifecycle.Deprecated || lifecycle == Lifecycle.Decommissioned)
            {
                var clock = new FakeTimeProvider();
                clock.SetUtcNow(origin.AddMinutes(i).AddHours(1));
                app.Deprecate(sunsetDate: clock.GetUtcNow().AddMinutes(1), clock);

                if (lifecycle == Lifecycle.Decommissioned)
                {
                    // Advance the same clock past the sunset date so Decommission's
                    // "now >= SunsetDate" invariant holds. Using one provider makes the
                    // temporal relationship explicit; otherwise a future tweak to the first
                    // clock could silently invalidate the decommission step.
                    clock.SetUtcNow(origin.AddMinutes(i).AddHours(2));
                    app.Decommission(clock);
                }
            }

            db.Applications.Add(app);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes application rows for a tenant whose <c>DisplayName</c> starts with
    /// <paramref name="namePrefix"/>. Use in test teardown when the test seeded
    /// rows with a unique (e.g., Guid-suffixed) prefix — preserves rows seeded
    /// by other tests in the same class fixture. Slice 6.
    /// <para>
    /// ADR-0098 / slice 8: previously this filtered on the <c>name</c> column;
    /// after the kebab slug was retired, callers pass display-name-prefix strings
    /// (via <see cref="SeedApplicationsAsync"/> / <see cref="SeedApplicationsWithLifecycleAsync"/>
    /// which now write to <c>display_name</c>), so we filter on <c>display_name</c>.
    /// </para>
    /// </summary>
    public async Task DeleteApplicationsByPrefixAsync(TenantId tenantId, string namePrefix)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM catalog_applications WHERE tenant_id = {0} AND display_name LIKE {1} || '%'",
            tenantId.Value, namePrefix);
    }

    /// <summary>
    /// Seeds a single Catalog application for a tenant and returns its id.
    /// Used by slice-8 assign-team tests that need exactly one row. ADR-0103:
    /// TeamId is required — when <paramref name="teamId"/> is null the app is
    /// owned by a fresh, unrelated team id (callers that don't care about team
    /// membership); pass an explicit team id when the test joins to a teams row.
    /// Bypass-RLS so RLS does not block the insert.
    /// </summary>
    public async Task<Guid> SeedSingleApplicationAsync(
        TenantId tenantId, Guid createdByUserId, Guid? teamId, string? namePrefix = null)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);

        var name = (namePrefix ?? "assign-app") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var app = DomainApplication.Create(
            displayName: name,
            description: "seeded for assign-team tests",
            createdByUserId: createdByUserId,
            teamId: teamId ?? Guid.NewGuid(),
            tenantId: tenantId,
            createdAt: DateTimeOffset.UtcNow);

        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return app.Id.Value;
    }

    /// <summary>
    /// Inserts a team row directly into the Organization schema via BYPASSRLS so
    /// the Catalog AssignApplicationTeam endpoint's cross-module existence check
    /// (<c>IOrganizationTeamExistenceChecker</c>) finds a real row in the active
    /// tenant. Returns the new team's id. Slice 8.
    /// </summary>
    public async Task<Guid> SeedTeamInOrganizationAsync(TenantId tenantId, string displayName)
    {
        var teamId = Guid.NewGuid();
        await using var conn = new Npgsql.NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO teams (id, tenant_id, display_name, description, created_at)
            VALUES ($1, $2, $3, NULL, NOW())
            """;
        cmd.Parameters.AddWithValue(teamId);
        cmd.Parameters.AddWithValue(tenantId.Value);
        cmd.Parameters.AddWithValue(displayName);
        await cmd.ExecuteNonQueryAsync();
        return teamId;
    }

    /// <summary>
    /// Seeds a <c>users</c> row in the Organization schema via BYPASSRLS. Slice 9
    /// / E1 — exercised by the Catalog list+detail handlers, which join through
    /// <c>IUserDirectory</c> to enrich <c>ApplicationResponse.Owner</c>. Mirrors
    /// <see cref="SeedTeamInOrganizationAsync"/> for consistency. Columns line up
    /// with <c>UserEntityTypeConfiguration</c> + the <c>AddUsersTable</c> migration.
    /// </summary>
    public async Task<Guid> SeedUserInOrganizationAsync(
        TenantId tenantId, string displayName, string email)
    {
        var userId = Guid.NewGuid();
        await SeedUserWithIdInOrganizationAsync(tenantId, userId, displayName, email);
        return userId;
    }

    /// <summary>
    /// Seeds a <c>users</c> row with an explicit <paramref name="userId"/> — used when the
    /// row must line up with a known JWT <c>sub</c> (e.g. to verify a handler enriches a
    /// creator resolved from the caller's token via <c>IUserDirectory</c>).
    /// </summary>
    public async Task SeedUserWithIdInOrganizationAsync(
        TenantId tenantId, Guid userId, string displayName, string email)
    {
        await using var conn = new Npgsql.NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id, tenant_id, email, display_name, created_at)
            VALUES ($1, $2, $3, $4, NOW())
            """;
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(tenantId.Value);
        cmd.Parameters.AddWithValue(email);
        cmd.Parameters.AddWithValue(displayName);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes a single <c>users</c> row via BYPASSRLS. Slice 9 / E1 cleanup hook
    /// for tests that seeded a directory user to verify Owner enrichment.
    /// </summary>
    public async Task DeleteUserInOrganizationAsync(Guid userId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = $1";
        cmd.Parameters.AddWithValue(userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds a team_members row directly via BYPASSRLS. <paramref name="roleByte"/>
    /// matches <c>TeamRole</c> (1 = Member, 2 = Admin). Idempotent.
    /// </summary>
    public async Task SeedTeamMembershipAsync(Guid teamId, Guid userId, byte roleByte)
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new OrganizationDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO team_members (team_id, user_id, role, added_at) VALUES ({0}, {1}, {2}, NOW()) ON CONFLICT (team_id, user_id) DO NOTHING",
            teamId, userId, (int)roleByte);
    }

    /// <summary>
    /// Forces an application's lifecycle column directly via BYPASSRLS, skipping
    /// domain transition guards. Slice 8 — used by the assign-team endpoint
    /// tests to put a freshly-seeded app into Decommissioned without driving
    /// through Active → Deprecated → Decommissioned via the HTTP endpoints.
    /// </summary>
    public async Task SetApplicationLifecycleAsync(Guid applicationId, Lifecycle lifecycle)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE catalog_applications SET lifecycle = {0} WHERE id = {1}",
            (short)lifecycle, applicationId);
    }

    /// <summary>
    /// Removes every team + team_members row for a tenant. Two-step is defensive
    /// (a follow-up FK migration adds <c>ON DELETE CASCADE</c>, so the explicit
    /// <c>team_members</c> wipe is redundant but harmless and remains explicit).
    /// </summary>
    public async Task DeleteTeamsForTenantAsync(Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new OrganizationDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM team_members WHERE team_id IN (SELECT id FROM teams WHERE tenant_id = {0})",
            tenantId);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM teams WHERE tenant_id = {0}",
            tenantId);
    }

    /// <summary>
    /// Seeds a single <c>CatalogSystem</c> grouping node directly via BYPASSRLS, bypassing
    /// the HTTP register endpoint's team-existence + authorization checks. Task 12/13
    /// (E-03.F-03.S-01) — used by Get/List/PartOf integration tests that need a System row
    /// without a full register round-trip each time. <paramref name="createdAt"/> defaults
    /// to now; pass an explicit spread-apart value for createdAt-sort pagination tests
    /// (mirrors <see cref="SeedApplicationsAsync"/>).
    /// </summary>
    public async Task<Guid> SeedSystemAsync(
        TenantId tenantId, Guid teamId, string displayName, Guid? createdByUserId = null, DateTimeOffset? createdAt = null)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);

        var system = DomainSystem.Create(
            displayName: displayName,
            description: "seeded for integration tests",
            createdByUserId: createdByUserId ?? Guid.NewGuid(),
            teamId: teamId,
            tenantId: tenantId,
            createdAt: createdAt ?? DateTimeOffset.UtcNow);

        db.Systems.Add(system);
        await db.SaveChangesAsync();
        return system.Id.Value;
    }

    /// <summary>Reads audit_log rows for a tenant via the BYPASSRLS pool, ordered by seq.</summary>
    public async Task<IReadOnlyList<AuditRowRecord>> ReadAuditLogAsync(Guid tenantId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seq, action, actor_id, actor_display, target_type, target_id,
                   data::text, prev_hash, row_hash, actor_type
            FROM audit_log WHERE tenant_id = $1 ORDER BY seq
            """;
        cmd.Parameters.AddWithValue(tenantId);
        var rows = new List<AuditRowRecord>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new AuditRowRecord(
                r.GetInt64(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetGuid(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                (byte[])r[7], (byte[])r[8],
                r.GetString(9)));
        }
        return rows;
    }

    public sealed record AuditRowRecord(
        long Seq, string Action, Guid? ActorId, string? ActorDisplay,
        string TargetType, string TargetId, string? DataJson, byte[] PrevHash, byte[] RowHash,
        string ActorType);
}
