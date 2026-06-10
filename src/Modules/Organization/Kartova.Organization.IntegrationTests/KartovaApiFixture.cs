using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Organization-specific fixture. All cross-module plumbing (Postgres container,
/// role bootstrap, JWT signer wiring, env-var wiring of the Kartova.Api host,
/// JWT minting helpers) lives in <see cref="KartovaApiFixtureBase"/>; this type
/// declares the DbContext to migrate plus the BYPASSRLS-seeding helper that
/// Organization integration tests use to plant rows for cross-tenant probes.
///
/// Slice 8 — migrates the Catalog schema too: the Organization module's
/// <c>DeleteTeamHandler</c> resolves the cross-module
/// <c>IApplicationCountByTeamReader</c>, which queries <c>catalog_applications</c>.
/// Without that table the DELETE /teams/{id} endpoint would throw on a missing
/// relation rather than evaluate the 409 team-has-applications branch.
/// </summary>
[ExcludeFromCodeCoverage]
public class KartovaApiFixture : KartovaApiFixtureBase
{
    // Slice 9 / H1-prereq: Organization integration tests exercise the real
    // Keycloak admin client via CreateInvitationHandler / RevokeInvitationHandler /
    // ExpireInvitationsHostedService. Opt into the shared Postgres + Keycloak
    // container pair so those handlers can provision and disable real KC users.
    protected override bool UsesKeycloakContainer => true;

    protected override async Task RunModuleMigrationsAsync(string migratorConnectionString)
    {
        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            migratorConnectionString,
            opts => new OrganizationDbContext(opts));
        await PostgresTestBootstrap.RunMigrationsAsync<CatalogDbContext>(
            migratorConnectionString,
            opts => new CatalogDbContext(opts));
    }

    public async Task<Guid> SeedOrganizationAsync(Guid tenantId, string name)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO organizations (id, tenant_id, name, created_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (id) DO UPDATE
                SET name = EXCLUDED.name,
                    tenant_id = EXCLUDED.tenant_id
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(name);
        var id = (Guid)(await cmd.ExecuteScalarAsync())!;
        return id;
    }

    /// <summary>
    /// Directly inserts a team row via the BYPASSRLS connection. Skips the
    /// <see cref="Domain.Team"/> static factory because it requires a TimeProvider
    /// and validates inputs we don't care to vary in integration tests — bypass-RLS
    /// SQL gets us a deterministic row in one statement. Returns the new team's id.
    /// Uses raw Npgsql rather than EF's <c>ExecuteSqlRawAsync</c> because EF cannot
    /// map a <see cref="DBNull"/> parameter (no CLR type to infer from).
    /// </summary>
    public async Task<Guid> SeedTeamAsync(Guid tenantId, string displayName, string? description = null)
    {
        var teamId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO teams (id, tenant_id, display_name, description, created_at)
            VALUES ($1, $2, $3, $4, NOW())
            """;
        cmd.Parameters.AddWithValue(teamId);
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(displayName);
        cmd.Parameters.AddWithValue((object?)description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return teamId;
    }

    /// <summary>
    /// Seeds a <c>users</c> row via BYPASSRLS. Slice 9 / E3 — used by team-detail
    /// and AddTeamMember integration tests so the new <c>TeamMemberResponse</c>
    /// <c>DisplayName</c> + <c>Email</c> fields populate from the
    /// <see cref="Kartova.SharedKernel.Identity.IUserDirectory"/> port. Mirrors the
    /// Catalog-side seeder added in E1. Columns line up with
    /// <c>UserEntityTypeConfiguration</c> + the <c>AddUsersTable</c> migration.
    /// <para>
    /// Slice-9 item 19 carry-forward: signature takes strong-typed
    /// <see cref="TenantId"/> to match the Catalog-side helper
    /// (<c>Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs</c>) — keeps
    /// cross-module test fixtures from drifting on the same operation.
    /// </para>
    /// </summary>
    public async Task<Guid> SeedUserInOrganizationAsync(
        TenantId tenantId, string displayName, string email, string realmRole = "Viewer", Guid? userId = null)
    {
        var resolvedUserId = userId ?? Guid.NewGuid();
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id, tenant_id, email, display_name, realm_role, created_at)
            VALUES ($1, $2, $3, $4, $5, NOW())
            """;
        cmd.Parameters.AddWithValue(resolvedUserId);
        cmd.Parameters.AddWithValue(tenantId.Value);
        cmd.Parameters.AddWithValue(email);
        cmd.Parameters.AddWithValue(displayName);
        cmd.Parameters.AddWithValue(realmRole);
        await cmd.ExecuteNonQueryAsync();
        return resolvedUserId;
    }

    /// <summary>
    /// Deletes a single <c>users</c> row via BYPASSRLS. Slice 9 / E3 cleanup hook —
    /// call BEFORE <see cref="DeleteTeamsForTenantAsync"/> per the slice-9
    /// ordering convention (E1 e5aaf73 / E2 4715c87): the more leak-prone
    /// direct-id delete (no prefix sweep) runs first so a downstream teams
    /// cleanup throw cannot strand a <c>users</c> row.
    /// </summary>
    public async Task DeleteUserInOrganizationAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = $1";
        cmd.Parameters.AddWithValue(userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts a team-membership row via BYPASSRLS. <paramref name="roleByte"/> matches
    /// <see cref="Domain.TeamRole"/> (1 = Member, 2 = Admin). Idempotent — duplicate
    /// (team_id, user_id) pairs are silently ignored, which is convenient for tests
    /// that seed a baseline and then overlay role changes.
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
    /// Cleans up every team and team_members row for <paramref name="tenantId"/>.
    /// Two-step is defensive — a follow-up migration adds the
    /// <c>team_members.team_id -> teams.id ON DELETE CASCADE</c> FK so the
    /// explicit child-row wipe is redundant but harmless.
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
    /// Inserts a Pending <c>invitations</c> row via BYPASSRLS. Slice 9 / H1 batch 4 —
    /// used by session-bootstrap tests to plant an invitation row tied to a specific
    /// <paramref name="keycloakUserId"/> (which doubles as the <c>sub</c> claim of
    /// the impersonated invitee's JWT) without round-tripping through the
    /// <c>POST /api/v1/organizations/invitations</c> handler (that path provisions a
    /// real KC user, which session-bootstrap tests don't need — the post-auth hook
    /// flips Pending → Accepted purely on DB state).
    /// Column names follow <c>InvitationEntityTypeConfiguration</c>. The handler's
    /// expiry guard at <c>PostAuthHook.cs:57</c> needs <c>ExpiresAt &gt; now</c> for
    /// the acceptance branch to fire, so callers should pass a future
    /// <paramref name="expiresAt"/> (default = invited_at + 7d).
    /// Status is hardcoded to <c>Pending</c> (smallint 1, matching
    /// <see cref="InvitationStatus.Pending"/>) — accepted/revoked/expired
    /// invitations don't drive the hook's acceptance path.
    /// </summary>
    public async Task<Guid> SeedInvitationAsync(
        Guid tenantId,
        string email,
        string role,
        Guid invitedByUserId,
        Guid keycloakUserId,
        DateTimeOffset invitedAt,
        DateTimeOffset? expiresAt = null)
    {
        var invitationId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO invitations
                (id, tenant_id, email, role, invited_by_user_id,
                 invited_at, expires_at, status, keycloak_user_id)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """;
        cmd.Parameters.AddWithValue(invitationId);
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(email);
        cmd.Parameters.AddWithValue(role);
        cmd.Parameters.AddWithValue(invitedByUserId);
        cmd.Parameters.AddWithValue(invitedAt);
        cmd.Parameters.AddWithValue(expiresAt ?? invitedAt.AddDays(7));
        cmd.Parameters.AddWithValue((short)InvitationStatus.Pending);
        cmd.Parameters.AddWithValue(keycloakUserId);
        await cmd.ExecuteNonQueryAsync();
        return invitationId;
    }

    /// <summary>
    /// Deletes every <c>invitations</c> row for <paramref name="tenantId"/> via the
    /// BYPASSRLS connection. Slice 9 / H1 — used by invitation integration tests so
    /// rows seeded indirectly through <c>POST /api/v1/organizations/invitations</c>
    /// can be cleaned up in <c>finally</c> blocks without leaking across tests.
    /// Mirrors <see cref="DeleteTeamsForTenantAsync"/>.
    /// </summary>
    public async Task DeleteInvitationsForTenantAsync(Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new OrganizationDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM invitations WHERE tenant_id = {0}",
            tenantId);
    }

    /// <summary>
    /// Deletes the <c>organizations</c> row for <paramref name="tenantId"/> via
    /// BYPASSRLS. Slice-9 H1 batch 2 cleanup hook — call from a test's
    /// <c>finally</c> block (typically AFTER child-table cleanup helpers like
    /// <see cref="DeleteUserInOrganizationAsync"/> /
    /// <see cref="DeleteInvitationsForTenantAsync"/> if the test seeded any).
    /// The <c>logo_*</c> columns live on this same row, so dropping it removes
    /// any uploaded logo at the same time (no separate logo table to clean up).
    /// The caller wraps in try/catch + logging — the fixture stays raw.
    /// </summary>
    public async Task DeleteOrganizationsForTenantAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM organizations WHERE tenant_id = $1";
        cmd.Parameters.AddWithValue(tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads the three <c>organizations.logo_*</c> columns
    /// (<c>logo_bytes</c>, <c>logo_mime_type</c>, <c>logo_content_hash</c>) for
    /// <paramref name="tenantId"/> via BYPASSRLS — bypasses the EF owned-entity
    /// mapping so a test can verify the columns are NULL after a rejected upload
    /// (the owned <c>Logo</c> property would just be null on the aggregate, but
    /// the columns under it are the actual source of truth). Returns
    /// (null, null, null) when no row exists for the tenant.
    /// </summary>
    public async Task<(byte[]? Bytes, string? MimeType, string? ContentHash)>
        ReadOrgLogoColumnsAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT logo_bytes, logo_mime_type, logo_content_hash "
                        + "FROM organizations WHERE tenant_id = $1";
        cmd.Parameters.AddWithValue(tenantId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return (null, null, null);
        }
        var bytes = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
        var mime = reader.IsDBNull(1) ? null : reader.GetString(1);
        var hash = reader.IsDBNull(2) ? null : reader.GetString(2);
        return (bytes, mime, hash);
    }

    /// <summary>
    /// Seeds one Catalog <see cref="DomainApplication"/> assigned to <paramref name="teamId"/>
    /// for the given tenant. Returns the new app's id. Used by
    /// <c>DeleteTeamTests</c> to drive the 409 team-has-applications branch:
    /// the Organization module's <c>DeleteTeamHandler</c> calls
    /// <c>IApplicationCountByTeamReader</c>, which runs against this row.
    /// Inserts via EF on a BYPASSRLS connection so RLS does not block the seed.
    /// </summary>
    public async Task<Guid> SeedCatalogApplicationAssignedToTeamAsync(
        Guid tenantId, Guid teamId, string name)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);

        var app = DomainApplication.Create(
            displayName: name,
            description: "seeded for DeleteTeam 409 path",
            ownerUserId: Guid.NewGuid(),
            tenantId: new TenantId(tenantId),
            createdAt: DateTimeOffset.UtcNow);
        app.AssignTeam(teamId);

        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return app.Id.Value;
    }

    /// <summary>
    /// Seeds one Catalog <see cref="DomainApplication"/> OWNED BY <paramref name="ownerUserId"/>
    /// for the given tenant (no team). Returns the new app's id. Slice-10 — retained for R2
    /// (rename OwnerUserId → CreatedByUserId) and other Catalog seeding needs. Inserts via EF on
    /// a BYPASSRLS connection so RLS does not block the seed. Mirrors
    /// <see cref="SeedCatalogApplicationAssignedToTeamAsync"/>.
    /// </summary>
    public async Task<Guid> SeedCatalogApplicationOwnedByAsync(
        Guid tenantId, Guid ownerUserId, string name)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);

        var app = DomainApplication.Create(
            displayName: name,
            description: "seeded for Offboard owner-reassignment path",
            ownerUserId: ownerUserId,
            tenantId: new TenantId(tenantId),
            createdAt: DateTimeOffset.UtcNow);

        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return app.Id.Value;
    }

    /// <summary>
    /// Reads the <c>owner_user_id</c> of a Catalog application via BYPASSRLS. Slice-10 Task 6 —
    /// lets <c>OffboardMemberTests</c> verify the owner was reassigned to the successor without
    /// going through the request-scoped (RLS-filtered) DbContext.
    /// </summary>
    public async Task<Guid?> ReadCatalogApplicationOwnerAsync(Guid applicationId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);
        var owners = await db.Applications
            .Where(a => EF.Property<Guid>(a, "_id") == applicationId)
            .Select(a => a.OwnerUserId)
            .ToListAsync();
        return owners.Count == 0 ? null : owners[0];
    }

    /// <summary>
    /// Deletes catalog application rows seeded via
    /// <see cref="SeedCatalogApplicationAssignedToTeamAsync"/> by tenant. Two-step
    /// because the catalog test fixture's per-app cleanup doesn't apply here.
    /// </summary>
    public async Task DeleteCatalogApplicationsForTenantAsync(Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;
        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM catalog_applications WHERE tenant_id = {0}",
            tenantId);
    }
}
