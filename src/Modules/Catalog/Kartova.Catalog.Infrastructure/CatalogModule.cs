using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
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

namespace Kartova.Catalog.Infrastructure;

[ExcludeFromCodeCoverage]
public sealed class CatalogModule : IModule, IModuleEndpoints
{
    public string Name => "catalog";

    public string Slug => "catalog";

    public Type DbContextType => typeof(CatalogDbContext);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var tenant = app.MapTenantScopedModule(Slug);     // /api/v1/catalog
        tenant.MapPost("/applications", CatalogEndpointDelegates.RegisterApplicationAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsRegister)
              .WithName("RegisterApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status201Created)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              // Membership gate (mirrors assign-team SF-2): non-OrgAdmin callers that
              // are not a member of the supplied teamId are rejected with 403 before
              // the handler runs. OrgAdmin is unaffected.
              .ProducesProblem(StatusCodes.Status403Forbidden)
              // ADR-0103: 422 invalid-team when the supplied teamId does not resolve
              // to a team in the current tenant.
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapGet("/applications/{id:guid}", CatalogEndpointDelegates.GetApplicationByIdAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetApplicationById")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/applications", CatalogEndpointDelegates.ListApplicationsAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("ListApplications")
              // CursorPage<T> envelope — ADR-0095: items + nextCursor + prevCursor.
              .Produces<CursorPage<ApplicationResponse>>(StatusCodes.Status200OK)
              // Slice 9 / E2 (spec §6.5), renamed slice 10 / ADR-0103: ?createdByUserId=
              // validation produces a 422 invalid-created-by envelope when the supplied
              // id does not resolve to a user in the current tenant (cross-tenant ids hit
              // the same branch because IUserDirectory is RLS-scoped).
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
              // sortBy/sortOrder enum schemas + bounded-integer limit schema are emitted
              // in the OpenAPI doc by Kartova.Api.OpenApi.CursorListQueryParameterTransformer
              // (registered in Program.cs). Endpoint binding stays `string?` so the custom
              // RFC 7807 envelopes (allowedFields, rawLimit) are preserved on parse failure;
              // the transformer keeps the wire schema honest for the generated TypeScript client.
        tenant.MapPut("/applications/{id:guid}", CatalogEndpointDelegates.EditApplicationAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsEditMetadata)
              .WithName("EditApplication")
              .AddEndpointFilter<IfMatchEndpointFilter>()
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict)
              .ProducesProblem(StatusCodes.Status412PreconditionFailed)
              .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        // POST deprecate — Active → Deprecated transition. Lifecycle endpoints don't
        // take If-Match (slice 5 spec §3 Decision #7); the domain invariant "current
        // state must be Active" is the implicit version.
        tenant.MapPost("/applications/{id:guid}/deprecate", CatalogEndpointDelegates.DeprecateApplicationAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleForward)
              .WithName("DeprecateApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        // POST decommission — Deprecated → Decommissioned transition. Empty body, no
        // If-Match (same rationale as deprecate). Two failure modes share the 409:
        // wrong source state, and "now < sunsetDate" — the latter carries
        // reason="before-sunset-date" + a sunsetDate extension member. No 400 path:
        // the empty body has nothing to validate.
        tenant.MapPost("/applications/{id:guid}/decommission", CatalogEndpointDelegates.DecommissionApplicationAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleForward)
              .WithName("DecommissionApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
        // PUT successor — set/clear successor while Deprecated (ADR-0110).
        // PUT = idempotent replacement (ADR-0096); null clears. Same forward
        // permission as deprecate/decommission (Member-or-OrgAdmin on the app's
        // team). 422 = unknown/cross-tenant successor id (delegate pre-check);
        // 409 = source not Deprecated; 400 = self-successor — both domain guards
        // inside Application.SetSuccessor().
        tenant.MapPut("/applications/{id:guid}/successor", CatalogEndpointDelegates.SetApplicationSuccessorAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleForward)
              .WithName("SetApplicationSuccessor")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        // POST reactivate — reverse lifecycle transition (Deprecated/Decommissioned → Active).
        // OrgAdmin only (CatalogApplicationsLifecycleReverse). Empty body, no If-Match —
        // same rationale as deprecate/decommission. The domain invariant inside
        // Application.Reactivate() rejects non-Deprecated/Decommissioned sources with
        // InvalidLifecycleTransitionException → 409 LifecycleConflict.
        tenant.MapPost("/applications/{id:guid}/reactivate", CatalogEndpointDelegates.ReactivateApplicationAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleReverse)
              .WithName("ReactivateApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
        // POST un-decommission — reverse lifecycle transition (Decommissioned → Deprecated).
        // OrgAdmin only (CatalogApplicationsLifecycleReverse). Takes a new sunsetDate body —
        // the transition re-enters Deprecated state so a future sunset is required. The domain
        // invariant inside Application.UnDecommission() rejects non-Decommissioned sources with
        // InvalidLifecycleTransitionException → 409, and a past sunsetDate throws
        // ArgumentException → 400 ValidationFailed (same as Deprecate).
        tenant.MapPost("/applications/{id:guid}/un-decommission", CatalogEndpointDelegates.UnDecommissionApplicationAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleReverse)
              .WithName("UnDecommissionApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
        tenant.MapPost("/services", CatalogEndpointDelegates.RegisterServiceAsync)
              .RequireAuthorization(KartovaPermissions.CatalogServicesRegister)
              .WithName("RegisterService")
              .Produces<ServiceResponse>(StatusCodes.Status201Created)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapGet("/services/{id:guid}", CatalogEndpointDelegates.GetServiceByIdAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetServiceById")
              .Produces<ServiceResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/relationships", CatalogEndpointDelegates.ListRelationshipsAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("ListRelationships")
              .Produces<CursorPage<RelationshipResponse>>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapGet("/graph", CatalogEndpointDelegates.GetCatalogGraphAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetCatalogGraph")
              .Produces<GraphResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden);
        tenant.MapDelete("/relationships/{id:guid}", CatalogEndpointDelegates.DeleteRelationshipAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRelationshipsWrite)
              .WithName("DeleteRelationship")
              .Produces(StatusCodes.Status204NoContent)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapPost("/relationships", CatalogEndpointDelegates.CreateRelationshipAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRelationshipsWrite)
              .WithName("CreateRelationship")
              .Produces<RelationshipResponse>(StatusCodes.Status201Created)
              .ProducesProblem(StatusCodes.Status401Unauthorized)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status409Conflict)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapGet("/services", CatalogEndpointDelegates.ListServicesAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("ListServices")
              .Produces<CursorPage<ServiceResponse>>(StatusCodes.Status200OK)
              // sortBy/sortOrder enum schemas + bounded-integer limit schema are emitted by
              // Kartova.Api.OpenApi.CursorListQueryParameterTransformer (same as ListApplications);
              // the C# binding stays string? so the RFC 7807 parse-failure envelopes survive.
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapPost("/apis", CatalogEndpointDelegates.RegisterApiAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApisRegister)
              .WithName("RegisterApi")
              .Produces<ApiResponse>(StatusCodes.Status201Created)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapGet("/apis/{id:guid}", CatalogEndpointDelegates.GetApiByIdAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetApiById")
              .Produces<ApiResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/apis", CatalogEndpointDelegates.ListApisAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("ListApis")
              .Produces<CursorPage<ApiResponse>>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        // PUT assign-team — set or clear Application.TeamId. Claim gate stops
        // Viewer/anon; the resource gate (ApplicationTeamScoped — OrgAdmin OR
        // member of the app's current team) runs inside the delegate against
        // the pre-loaded app. 422 invalid-team surfaces when the target team
        // does not exist in the active tenant scope (slice 8 / ADR-0098 §6.4).
        tenant.MapPut("/applications/{id:guid}/team", CatalogEndpointDelegates.AssignApplicationTeamAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApplicationsEditMetadata)
              .WithName("AssignApplicationTeam")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext per ADR-0090. Connection flows from ITenantScope —
        // raw AddDbContext would silently bypass RLS for any future Catalog entity.
        services.AddModuleDbContext<CatalogDbContext>(npg =>
            npg.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName));

        // Handler is invoked directly from the endpoint delegate (synchronous,
        // in-process) so it must be resolvable from the HTTP request scope
        // alongside CatalogDbContext / ITenantContext / ICurrentUser. See the
        // comment on CatalogEndpointDelegates.RegisterApplicationAsync.
        services.AddScoped<RegisterApplicationHandler>();
        services.AddScoped<GetApplicationByIdHandler>();
        services.AddScoped<ListApplicationsHandler>();
        services.AddScoped<EditApplicationHandler>();
        services.AddScoped<DeprecateApplicationHandler>();
        services.AddScoped<DecommissionApplicationHandler>();
        services.AddScoped<SetApplicationSuccessorHandler>();
        services.AddScoped<ReactivateApplicationHandler>();
        services.AddScoped<UnDecommissionApplicationHandler>();
        services.AddScoped<AssignApplicationTeamHandler>();
        services.AddScoped<RegisterServiceHandler>();
        services.AddScoped<GetServiceByIdHandler>();
        services.AddScoped<ListServicesHandler>();
        services.AddScoped<RegisterApiHandler>();
        services.AddScoped<GetApiByIdHandler>();
        services.AddScoped<ListApisHandler>();
        services.AddScoped<CreateRelationshipHandler>();
        services.AddScoped<DeleteRelationshipHandler>();
        services.AddScoped<ListRelationshipsForEntityHandler>();
        services.AddScoped<GraphTraversalHandler>();
        services.AddScoped<ICatalogEntityLookup, CatalogEntityLookup>();

        // TimeProvider is needed by Application.Deprecate / Decommission for the
        // "sunsetDate must be in the future" / "now >= sunsetDate" checks. TryAdd
        // is idempotent — if another module (or test fixture override) already
        // registered TimeProvider, this is a no-op so tests can swap in
        // FakeTimeProvider without losing the production default.
        services.TryAddSingleton(TimeProvider.System);

        // Cross-module readers exposed to Organization via SharedKernel ports
        // (slice 8). The Organization module never references Catalog directly —
        // it depends on IApplicationCountByTeamReader / IApplicationsByTeamReader,
        // implemented here against the Catalog DbContext.
        services.AddScoped<IApplicationCountByTeamReader, ApplicationCountByTeamReader>();
        services.AddScoped<IApplicationsByTeamReader, ApplicationsByTeamReader>();
    }

    public void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
    {
        var cs = KartovaConnectionStrings.RequireMain(configuration);

        services.AddDbContext<CatalogDbContext>(opts =>
            opts.UseNpgsql(cs, npg => npg.MigrationsAssembly(
                typeof(CatalogDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        // Discovery only — synchronous HTTP handlers use direct dispatch (ADR-0093).
        // This call keeps the assembly visible to Wolverine for future async/event handlers
        // and outbox-driven publishes once the catalog starts emitting domain events.
        options.Discovery.IncludeAssembly(typeof(CatalogModule).Assembly);
    }
}
