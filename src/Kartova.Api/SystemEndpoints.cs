using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.AspNetCore.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Api;

/// <summary>
/// System-level routes owned by no module: the ADR-0060 health probes, the OpenAPI document and the
/// version endpoint. Mapped through <see cref="IModuleEndpoints"/> rather than directly in Program.cs so
/// that EndpointRouteRules sweeps them like every module route (TD-015).
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SystemEndpoints : IModuleEndpoints
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("live"),
            ResponseWriter = HealthCheckJsonResponseWriter.WriteCompactAsync,
        }).WithName("HealthLive");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("ready"),
            ResponseWriter = HealthCheckJsonResponseWriter.WriteCompactAsync,
        }).WithName("HealthReady");
        app.MapHealthChecks("/health/startup", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("startup"),
            ResponseWriter = HealthCheckJsonResponseWriter.WriteCompactAsync,
        }).WithName("HealthStartup");
        // Operator-only (ADR-0118) but kept at its ADR-0060 URL; allowlisted in EndpointRouteRules.OpsAdminRoutes.
        app.MapHealthChecks("/health/detailed", new HealthCheckOptions
        {
            ResponseWriter = HealthCheckJsonResponseWriter.WriteDetailedAsync,
        }).RequireAuthorization(PlatformAdminAuth.Policy)
          .WithName("HealthDetailed");

        // OpenAPI document endpoint — anonymous, no auth requirement (ADR-0029/0034).
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous().WithName("GetOpenApiDocument");

        app.MapGet("/api/v1/version", GetVersion).AllowAnonymous().WithName("GetVersion");
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
