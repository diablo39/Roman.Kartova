using System.Diagnostics.CodeAnalysis;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Organization bounded-context module (ADR-0082).
///
/// Registers the tenant-scoped <see cref="OrganizationDbContext"/> via
/// <c>AddModuleDbContext</c> so it participates in the per-request
/// <c>ITenantScope</c> (shared connection + transaction, <c>SET LOCAL app.current_tenant_id</c>)
/// per ADR-0090.
///
/// The admin bypass path (<see cref="Admin.AdminOrganizationDbContext"/>) is registered
/// separately by <c>Kartova.Organization.Infrastructure.Admin</c>'s composition extension
/// (<c>AddOrganizationAdmin</c>) because <c>Infrastructure</c> cannot reference
/// <c>Infrastructure.Admin</c> without creating a circular project reference.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class OrganizationModule : IModule, IModuleEndpoints
{
    public string Name => "organization";

    public string Slug => "organizations";

    public Type DbContextType => typeof(OrganizationDbContext);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var tenant = app.MapTenantScopedModule(Slug);     // /api/v1/organizations
        tenant.MapGet("/me", OrganizationEndpointDelegates.GetMeAsync)
            .WithName("GetOrganizationMe")
            .Produces<OrganizationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/me/permissions", OrganizationEndpointDelegates.GetMePermissions)
            .WithName("GetMePermissions")
            .Produces<MePermissionsResponse>(StatusCodes.Status200OK);
        tenant.MapGet("/me/admin-only", OrganizationEndpointDelegates.GetAdminOnlyAsync)
            .RequireAuthorization(p => p.RequireRole(KartovaRoles.OrgAdmin))
            .WithName("GetOrganizationMeAdminOnly")
            .Produces<AdminOnlyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // ---- Team management (slice 8, ADR-0098 / spec §6) -----------------
        // Claim-policy gate via .RequireAuthorization stops Viewer/anon.
        // Resource-auth gate (TeamAdminOfThis) is enforced inside the delegate
        // via IAuthorizationService — applied ONLY to mutation endpoints. The
        // bare GETs and POST /teams rely on the claim gate alone: CreateTeam
        // has no target team yet, and the read-list / read-detail surfaces are
        // visible to any tenant member with team.read.

        tenant.MapGet("/teams", OrganizationEndpointDelegates.ListTeamsAsync)
            .RequireAuthorization(KartovaPermissions.TeamRead)
            .WithName("ListTeams")
            // CursorPage<T> envelope — ADR-0095: items + nextCursor + prevCursor.
            // sortBy/sortOrder enum schemas + bounded-integer limit schema are emitted
            // by the OpenAPI transformer wired in Program.cs (same path as catalog).
            .Produces<CursorPage<TeamResponse>>(StatusCodes.Status200OK);

        tenant.MapGet("/teams/{id:guid}", OrganizationEndpointDelegates.GetTeamAsync)
            .RequireAuthorization(KartovaPermissions.TeamRead)
            .WithName("GetTeam")
            .Produces<TeamDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapPost("/teams", OrganizationEndpointDelegates.CreateTeamAsync)
            .RequireAuthorization(KartovaPermissions.TeamCreate)
            .WithName("CreateTeam")
            .Produces<TeamResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        tenant.MapPut("/teams/{id:guid}", OrganizationEndpointDelegates.UpdateTeamAsync)
            .RequireAuthorization(KartovaPermissions.TeamMetadataEdit)
            .WithName("UpdateTeam")
            .Produces<TeamResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapDelete("/teams/{id:guid}", OrganizationEndpointDelegates.DeleteTeamAsync)
            .RequireAuthorization(KartovaPermissions.TeamDelete)
            .WithName("DeleteTeam")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // 409 team-has-applications with applicationCount extension — spec §6.5.
            .ProducesProblem(StatusCodes.Status409Conflict);

        tenant.MapPost("/teams/{id:guid}/members", OrganizationEndpointDelegates.AddTeamMemberAsync)
            .RequireAuthorization(KartovaPermissions.TeamMembersManage)
            .WithName("AddTeamMember")
            // 201 + TeamMemberResponse body (spec critic-revision §7 — NOT 204).
            .Produces<TeamMemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        tenant.MapDelete("/teams/{id:guid}/members/{userId:guid}", OrganizationEndpointDelegates.RemoveTeamMemberAsync)
            .RequireAuthorization(KartovaPermissions.TeamMembersManage)
            .WithName("RemoveTeamMember")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapPut("/teams/{id:guid}/members/{userId:guid}", OrganizationEndpointDelegates.UpdateTeamMemberAsync)
            .RequireAuthorization(KartovaPermissions.TeamMembersManage)
            .WithName("UpdateTeamMember")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext — connection flows from ITenantScope per ADR-0090.
        // Migrations assembly pinned so `dotnet ef` and runtime agree.
        services.AddModuleDbContext<OrganizationDbContext>(npg =>
            npg.MigrationsAssembly(typeof(OrganizationDbContext).Assembly.FullName));

        services.AddScoped<IOrganizationQueries, OrganizationQueries>();

        // TimeProvider is needed by Organization.Create and any future
        // organization-side handler. TryAdd is idempotent — if another module
        // (or test fixture override) already registered TimeProvider, this is a
        // no-op so tests can swap in FakeTimeProvider without losing the
        // production default. Mirrors CatalogModule.RegisterServices.
        services.TryAddSingleton(TimeProvider.System);

        // Team-membership reader: populates ICurrentUser.TeamMemberships from team_members.
        // Invoked from TenantScopeBeginMiddleware after BeginAsync (slice 8 / ADR-0098).
        services.AddScoped<ITeamMembershipReader, OrganizationTeamMembershipReader>();

        // Cross-module team-existence checker (slice 8). Consumed by Catalog's
        // AssignApplicationTeamHandler — Catalog never references Organization
        // directly, only the IOrganizationTeamExistenceChecker port in
        // Kartova.SharedKernel.Multitenancy.
        services.AddScoped<IOrganizationTeamExistenceChecker, OrganizationTeamExistenceChecker>();

        // Team CRUD + member handlers (slice 8). Handlers are invoked directly
        // from the endpoint delegate (synchronous, in-process) — same dispatch
        // pattern as CatalogModule, see CatalogEndpointDelegates' class comment.
        services.AddScoped<CreateTeamHandler>();
        services.AddScoped<UpdateTeamHandler>();
        services.AddScoped<DeleteTeamHandler>();
        services.AddScoped<AddTeamMemberHandler>();
        services.AddScoped<RemoveTeamMemberHandler>();
        services.AddScoped<UpdateTeamMemberHandler>();
        services.AddScoped<GetTeamHandler>();
        services.AddScoped<ListTeamsHandler>();

        // Post-auth hook: upserts `users` projection from JWT claims + detects invitation
        // acceptance for the current request (spec §4.3, §5.2). Resolved by
        // TenantClaimsTransformation via IEnumerable<IPostAuthSyncHook>.
        services.AddScoped<UserProjectionUpdater>();
        services.AddScoped<IPostAuthSyncHook, OrganizationPostAuthSyncHook>();

        // Cross-module port (ADR-0098 + slice-9 spec §3): exposes the local users
        // projection so Catalog/Team responses can attach display names + emails
        // without referencing Organization internals.
        services.AddScoped<IUserDirectory, OrganizationUserDirectory>();
    }

    /// <summary>
    /// Migrator-specific registration: plain <c>AddDbContext</c> (no tenant scope).
    /// Migrations are DDL and run under the migrator role with RLS-bypass; they do
    /// not need the per-request shared connection/transaction from <c>ITenantScope</c>
    /// that <c>AddModuleDbContext</c> otherwise requires.
    /// </summary>
    public void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
    {
        var cs = KartovaConnectionStrings.RequireMain(configuration);

        services.AddDbContext<OrganizationDbContext>(opts =>
            opts.UseNpgsql(cs, npg => npg.MigrationsAssembly(
                typeof(OrganizationDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(OrganizationModule).Assembly);
        // Bus-routed handlers and publish routes arrive in later slices; slice 8
        // handlers use direct dispatch per ADR-0093.
    }
}
