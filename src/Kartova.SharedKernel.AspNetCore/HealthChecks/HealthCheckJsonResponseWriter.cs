using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.SharedKernel.AspNetCore.HealthChecks;

/// <summary>
/// ADR-0060 response shape (`status`/`totalDuration`/`entries[]`). The default
/// ASP.NET Core MapHealthChecks writer emits a bare status string, not this
/// JSON shape — this is the writer that produces it.
/// Compact mode (public probes) omits `exception` and, when an entry's own
/// `Exception` is non-null, nulls out `description` too (the framework's
/// exception-message fallback would otherwise leak through it). Detailed mode
/// (auth-gated /health/detailed) includes the exception message only (no stack
/// trace) and leaves `description` untouched.
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
                    // The framework catches an uncaught IHealthCheck exception and commonly
                    // sets Description to the exception's own message — suppress it here so a
                    // public, unauthenticated probe (/health/live|ready|startup) never leaks raw
                    // exception text (hostnames/connection details). A check's own deliberately
                    // authored Description (Exception is null) is left untouched.
                    description = e.Value.Exception is null ? e.Value.Description : null,
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
