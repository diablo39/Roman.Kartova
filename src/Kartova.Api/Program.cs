using System.Reflection;
using JasperFx;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Postgres;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine;
using Wolverine.Postgresql;

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

var kartovaConnection = builder.Configuration.GetConnectionString("Kartova")
    ?? throw new InvalidOperationException("ConnectionStrings__Kartova missing");

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
var bypassConnection = builder.Configuration.GetConnectionString("KartovaBypass")
    ?? throw new InvalidOperationException("ConnectionStrings__KartovaBypass missing");
builder.Services.AddDbContext<AdminOrganizationDbContext>(opts => opts.UseNpgsql(bypassConnection));
builder.Services.AddScoped<IAdminOrganizationCommands, AdminOrganizationCommands>();

// Wolverine — persistence only; no message routing in Slice 2.
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine");

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

app.UseStatusCodePages();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Anonymous version endpoint.
app.MapGet("/api/v1/version", () =>
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
}).AllowAnonymous();

// Tenant-scoped routes.
var tenantScoped = app.MapGroup("/api/v1").RequireTenantScope();
Kartova.Api.Endpoints.OrganizationEndpoints.Map(tenantScoped);

// Admin (non-tenant) routes — platform-admin only.
var admin = app.MapGroup("/api/v1/admin").RequireAuthorization(policy => policy.RequireRole("platform-admin"));
Kartova.Api.Endpoints.AdminOrganizationEndpoints.Map(admin);

return await app.RunJasperFxCommands(args);
