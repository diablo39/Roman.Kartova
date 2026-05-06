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
