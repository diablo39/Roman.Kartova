using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
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
              .WithName("RegisterApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status201Created)
              .ProducesProblem(StatusCodes.Status400BadRequest);
        tenant.MapGet("/applications/{id:guid}", CatalogEndpointDelegates.GetApplicationByIdAsync)
              .WithName("GetApplicationById")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/applications", CatalogEndpointDelegates.ListApplicationsAsync)
              .WithName("ListApplications")
              // CursorPage<T> envelope — ADR-0095: items + nextCursor + prevCursor.
              .Produces<CursorPage<ApplicationResponse>>(StatusCodes.Status200OK);
              // sortBy/sortOrder enum schemas + bounded-integer limit schema are emitted
              // in the OpenAPI doc by Kartova.Api.OpenApi.CursorListQueryParameterTransformer
              // (registered in Program.cs). Endpoint binding stays `string?` so the custom
              // RFC 7807 envelopes (allowedFields, rawLimit) are preserved on parse failure;
              // the transformer keeps the wire schema honest for the generated TypeScript client.
        tenant.MapPut("/applications/{id:guid}", CatalogEndpointDelegates.EditApplicationAsync)
              .WithName("EditApplication")
              .AddEndpointFilter<IfMatchEndpointFilter>()
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict)
              .ProducesProblem(StatusCodes.Status412PreconditionFailed)
              .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        // POST deprecate — Active → Deprecated transition. Lifecycle endpoints don't
        // take If-Match (slice 5 spec §3 Decision #7); the domain invariant "current
        // state must be Active" is the implicit version.
        tenant.MapPost("/applications/{id:guid}/deprecate", CatalogEndpointDelegates.DeprecateApplicationAsync)
              .WithName("DeprecateApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
        // POST decommission — Deprecated → Decommissioned transition. Empty body, no
        // If-Match (same rationale as deprecate). Two failure modes share the 409:
        // wrong source state, and "now < sunsetDate" — the latter carries
        // reason="before-sunset-date" + a sunsetDate extension member. No 400 path:
        // the empty body has nothing to validate.
        tenant.MapPost("/applications/{id:guid}/decommission", CatalogEndpointDelegates.DecommissionApplicationAsync)
              .WithName("DecommissionApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
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

        // TimeProvider is needed by Application.Deprecate / Decommission for the
        // "sunsetDate must be in the future" / "now >= sunsetDate" checks. TryAdd
        // is idempotent — if another module (or test fixture override) already
        // registered TimeProvider, this is a no-op so tests can swap in
        // FakeTimeProvider without losing the production default.
        services.TryAddSingleton(TimeProvider.System);
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
