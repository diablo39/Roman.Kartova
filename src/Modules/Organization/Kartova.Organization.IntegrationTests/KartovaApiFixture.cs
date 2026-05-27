using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
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
