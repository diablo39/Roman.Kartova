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

    /// <summary>
    /// Composes the Organization module's HTTP surface from per-resource route
    /// extension methods. Each <c>*Routes.MapTo</c> colocates with its owning
    /// <c>*EndpointDelegates.cs</c> file (slice-9 carry-forward S6 — extends
    /// the H5 R2 per-resource delegate split to the route registrations too).
    /// <para>
    /// <see cref="AuthRoutes.MapTo"/> takes the raw <see cref="IEndpointRouteBuilder"/>
    /// rather than the tenant group because <c>/api/v1/auth/session</c> sits
    /// outside the <c>/api/v1/organizations</c> slug (per-request bootstrap,
    /// not an organization resource).
    /// </para>
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var tenant = app.MapTenantScopedModule(Slug);     // /api/v1/organizations
        OrganizationProfileRoutes.MapTo(tenant);
        TeamRoutes.MapTo(tenant);
        InvitationRoutes.MapTo(tenant);
        UserRoutes.MapTo(tenant);
        AuthRoutes.MapTo(app);
    }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext — connection flows from ITenantScope per ADR-0090.
        // Migrations assembly pinned so `dotnet ef` and runtime agree.
        services.AddModuleDbContext<OrganizationDbContext>(npg =>
            npg.MigrationsAssembly(typeof(OrganizationDbContext).Assembly.FullName));

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
        // Org profile read/write (slice-9 spec §4). Lives in Infrastructure
        // (not Application) because both classes depend on OrganizationDbContext —
        // same placement as the slice-8 team handlers above.
        services.AddScoped<OrgProfileQueries>();
        services.AddScoped<UpdateOrgProfileHandler>();

        // User typeahead search + detail (slice-9 spec §6.7) — read-only
        // queries against the local users projection joined with team_members.
        services.AddScoped<UserQueries>();

        // Logo upload/clear/serve handler (slice-9 spec §6.4) — same Infrastructure
        // placement as the sibling profile handlers above.
        services.AddScoped<LogoCommands>();

        // Session bootstrap handler (slice 9 spec §6.7 / §9.8) — composes the
        // org-profile read with the invitation lookup so the SPA can hydrate a
        // fresh session in one round-trip. Same Infrastructure placement as the
        // queries it composes — depends on OrganizationDbContext.
        services.AddScoped<SessionStartHandler>();

        services.AddScoped<CreateTeamHandler>();
        services.AddScoped<UpdateTeamHandler>();
        services.AddScoped<DeleteTeamHandler>();
        services.AddScoped<AddTeamMemberHandler>();
        services.AddScoped<RemoveTeamMemberHandler>();
        services.AddScoped<UpdateTeamMemberHandler>();
        services.AddScoped<GetTeamHandler>();
        services.AddScoped<ListTeamsHandler>();
        services.AddScoped<ListMembersHandler>();

        // Invitation lifecycle handlers (slice 9 spec §6.7) — same direct-
        // dispatch pattern as the Team handlers above.
        services.AddScoped<CreateInvitationHandler>();
        services.AddScoped<RevokeInvitationHandler>();
        services.AddScoped<ListInvitationsHandler>();

        // Users-projection upsert helper — invoked inline by SessionStartHandler
        // on POST /api/v1/auth/session to materialize the local users row from
        // the JWT's OIDC claims (sub/email/given_name/family_name). The SPA's
        // OidcCallbackHandler always hits /auth/session first after the KC
        // roundtrip, so no separate per-request pipeline hook is needed.
        services.AddScoped<UserProjectionUpdater>();

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
