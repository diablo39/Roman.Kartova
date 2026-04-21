using System.Reflection;
using JasperFx;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// Module registry — explicit list; Slice 1 has only Catalog.
IModule[] modules =
[
    new CatalogModule(),
];

// Register each module's services.
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var kartovaConnection = builder.Configuration.GetConnectionString("Kartova")
    ?? throw new InvalidOperationException("ConnectionStrings__Kartova missing");

// Wolverine bootstrap — persistence only, no handlers or Kafka routing yet.
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine");

    foreach (var module in modules)
    {
        module.ConfigureWolverine(opts);
    }
});

// Health checks — three probes per ADR-0060.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(kartovaConnection, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

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
});

return await app.RunJasperFxCommands(args);
