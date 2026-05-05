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
              // Note: sortBy and sortOrder appear in the generated OpenAPI doc as plain
              // type:string (no enum constraint). This is a known limitation of using raw
              // string parameters to work around .NET 10 minimal-API case-sensitive enum
              // binding (Task 10). The WithOpenApi(transform) overload is deprecated in
              // .NET 10 (ASPDEPR002); the operation-transformer replacement would require
              // wiring in Program.cs and is out of scope for this task. Runtime safety is
              // enforced by the server-side Enum.TryParse + PagingExceptionHandler (→ RFC 7807
              // 400) and by useListUrlState's allowlist on the frontend. ADR-0095.
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
