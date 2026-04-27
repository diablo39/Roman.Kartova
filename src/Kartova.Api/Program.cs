using System.Reflection;
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

        var kartovaConnection = builder.Configuration.GetConnectionString(KartovaConnectionStrings.Main)
            ?? throw new InvalidOperationException($"ConnectionStrings__{KartovaConnectionStrings.Main} missing");

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

        // Admin bypass DbContext — separate BYPASSRLS connection string (ADR-0090).
        // Registered here (not in OrganizationModule) because OrganizationModule.Infrastructure
        // cannot project-reference Infrastructure.Admin (would be circular).
        var bypassConnection = builder.Configuration.GetConnectionString(KartovaConnectionStrings.Bypass)
            ?? throw new InvalidOperationException($"ConnectionStrings__{KartovaConnectionStrings.Bypass} missing");
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

        app.UseExceptionHandler();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<TenantScopeMiddleware>();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
        app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

        // Anonymous version endpoint.
        app.MapGet("/api/v1/version", GetVersion).AllowAnonymous();

        // Tenant-scoped routes.
        var tenantScoped = app.MapGroup("/api/v1").RequireTenantScope();
        Endpoints.OrganizationEndpoints.Map(tenantScoped);

        // Admin (non-tenant) routes — platform-admin only.
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization(policy => policy.RequireRole(KartovaRoles.PlatformAdmin));
        Endpoints.AdminOrganizationEndpoints.Map(admin);

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
