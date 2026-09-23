using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.SharedKernel.AspNetCore.HealthChecks;

/// <summary>
/// ADR-0060 response shape (`status`/`totalDuration`/`entries[]`). The default
/// ASP.NET Core MapHealthChecks writer emits a bare status string, not this
/// JSON shape — this is the writer that produces it.
/// Compact mode (public probes) omits `exception`; detailed mode (auth-gated
/// /health/detailed) includes the exception message only (no stack trace).
/// </summary>
public static class HealthCheckJsonResponseWriter
{
    public static Task WriteCompactAsync(HttpContext context, HealthReport report) => WriteAsync(context, report, detailed: false);

    public static Task WriteDetailedAsync(HttpContext context, HealthReport report) => WriteAsync(context, report, detailed: true);

    private static Task WriteAsync(HttpContext context, HealthReport report, bool detailed)
    {
        context.Response.ContentType = "application/json";

        var entries = report.Entries.ToDictionary(
            e => e.Key,
            object (e) => detailed
                ? new
                {
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.ToString(),
                    tags = e.Value.Tags,
                    description = e.Value.Description,
                    exception = e.Value.Exception?.Message,
                }
                : new
                {
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.ToString(),
                    tags = e.Value.Tags,
                    description = e.Value.Description,
                });

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.ToString(),
            entries,
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
