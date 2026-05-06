using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Application;
using Kartova.Organization.Infrastructure;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine;

namespace Kartova.Api;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Module registry.
        IModule[] modules =
        [
            new CatalogModule(),
            new OrganizationModule(),
        ];

        foreach (var module in modules)
        {
            module.RegisterServices(builder.Services, builder.Configuration);
        }

        var kartovaConnection = KartovaConnectionStrings.RequireMain(builder.Configuration);

        // NpgsqlDataSource — used by TenantScope to open pooled connections.
        builder.Services.AddNpgsqlDataSource(kartovaConnection);

        // Tenant scope + required interceptor — ADR-0090.
        builder.Services.AddTenantScope();

        // JWT authentication — ADR-0006/0007/0014 + claims transformation populates ITenantContext.
        builder.Services.AddKartovaJwtAuth(builder.Configuration);
        builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

        // RFC 7807 problem details — ADR-0091.
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["traceId"] =
                    System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
            };
        });

        // OpenAPI document — /openapi/v1.json (anonymous, no Swashbuckle, ADR-0029/0034).
        // Sort query-parameter enum surfacing (ADR-0095): see SortQueryEnumTransformer
        // for why the wire-level binding stays `string?` while OpenAPI gets enum values.
        builder.Services.AddOpenApi("v1", o =>
        {
            o.AddOperationTransformer<Kartova.Api.OpenApi.SortQueryEnumTransformer>();
        });

        // Emit enum values as camelCase strings in JSON and OpenAPI (ADR-0095).
        // Applies globally so every current and future enum (ApplicationSortField,
        // SortOrder, per-resource sort fields for Components/Services/Libraries, etc.)
        // is covered without per-enum attributes.
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        // CORS — allow configured SPA origins (e.g. http://localhost:5173 in dev).
        // Production default: empty array → all origins blocked (safe default).
        // No AllowCredentials: SPA carries the access token in the `Authorization: Bearer` header,
        // not in cookies. Re-introduce only when the BFF cookie-session story (E-01.F-04.S-05) lands.
        var corsOrigins = builder.Configuration
            .GetSection(CorsConfigKeys.AllowedOrigins).Get<string[]>() ?? [];
        if (corsOrigins.Length == 0 && !builder.Environment.IsDevelopment())
        {
            // Surface misconfiguration loudly in non-Development environments.
            // Empty allowlist produces silent browser breakage (no Allow-Origin header) — log so ops sees it.
            using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
            startupLoggerFactory.CreateLogger("Kartova.Cors")
                .LogWarning("Cors:AllowedOrigins is empty in environment {Environment}; all browser SPA requests will be blocked.",
                    builder.Environment.EnvironmentName);
        }
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("KartovaWeb", policy =>
            {
                if (corsOrigins.Length == 0)
                {
                    return;
                }
                policy.WithOrigins(corsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        // Domain-validation → 400 mapping — slice-3 spec §13.3.
        // Maps ArgumentException (thrown by aggregate factories) to RFC 7807 400.
        // Centralized so write endpoints don't copy-paste a try/catch.
        builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();

        // Pagination/sort → 400 mapping — ADR-0095.
        // Maps InvalidSortFieldException and InvalidCursorException to RFC 7807 400.
        builder.Services.AddExceptionHandler<PagingExceptionHandler>();

        // Admin bypass DbContext — separate BYPASSRLS connection string (ADR-0090).
        // Registered here (not in OrganizationModule) because OrganizationModule.Infrastructure
        // cannot project-reference Infrastructure.Admin (would be circular).
        var bypassConnection = KartovaConnectionStrings.RequireBypass(builder.Configuration);
        builder.Services.AddDbContext<AdminOrganizationDbContext>(opts => opts.UseNpgsql(bypassConnection));
        builder.Services.AddScoped<IAdminOrganizationCommands, AdminOrganizationCommands>();

        // Wolverine — in-process CQRS mediator only.
        // Postgres persistence (outbox) is deferred until a slice publishes domain events.
        // See ADR-0080 and docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md.
        // When persistence is re-enabled, the `wolverine.*` schema must be created by
        // Kartova.Migrator (ADR-0085), and API-side auto-create must be disabled at the
        // same time.
        builder.Host.UseWolverine(opts =>
        {
            foreach (var module in modules)
            {
                module.ConfigureWolverine(opts);
            }
        });

        // Health checks — ADR-0060.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddNpgSql(kartovaConnection, name: "postgres", tags: ["ready"]);

        var app = builder.Build();

        // ADR-0091: rely on UseExceptionHandler + AddProblemDetails to emit application/problem+json
        // for unhandled exceptions and bodyless status codes. UseStatusCodePages would emit
        // text/plain bodies for 4xx/5xx without explicit bodies and shadow that contract.
        app.UseExceptionHandler();

        app.UseCors("KartovaWeb");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<TenantScopeBeginMiddleware>();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
        app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

        // OpenAPI document endpoint — anonymous, no auth requirement (ADR-0029/0034).
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

        // Anonymous version endpoint — system-level, not module-owned.
        app.MapGet("/api/v1/version", GetVersion).AllowAnonymous();

        // Module endpoints — each module wires its own routes via IModuleEndpoints.
        // OrganizationAdminModule is grafted in separately because its endpoints
        // depend on IAdminOrganizationCommands from Kartova.Organization.Infrastructure.Admin,
        // which already references Kartova.Organization.Infrastructure (where IModule lives).
        // Putting MapEndpoints for admin routes inside OrganizationModule would require the
        // reverse reference, creating a project cycle.
        IModuleEndpoints[] endpointModules =
        [
            .. modules.OfType<IModuleEndpoints>(),
            new OrganizationAdminModule(),
        ];
        foreach (var module in endpointModules)
        {
            module.MapEndpoints(app);
        }

        if (app.Environment.IsEnvironment("Testing"))
        {
            await app.RunAsync();
            return 0;
        }

        return await app.RunJasperFxCommands(args);
    }

    private static IResult GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "0.1.0";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var commit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown";
        var buildTime = Environment.GetEnvironmentVariable("BUILD_TIME") ?? DateTimeOffset.UtcNow.ToString("O");

        return Results.Ok(new
        {
            version = informationalVersion ?? version,
            commit,
            buildTime,
        });
    }
}
