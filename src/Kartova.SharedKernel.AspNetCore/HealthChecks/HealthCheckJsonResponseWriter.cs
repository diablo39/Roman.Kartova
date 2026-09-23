using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.SharedKernel.AspNetCore.HealthChecks;

/// <summary>
/// ADR-0060 response shape (`status`/`totalDuration`/`entries[]`). The default
/// ASP.NET Core MapHealthChecks writer emits a bare status string, not this
/// JSON shape — this is the writer that produces it.
/// Compact mode (public probes) omits `exception`. It also nulls out
/// `description` — but ONLY when the entry's `Description` is literally the
/// framework's own exception-message fallback (`Description == Exception.Message`,
/// set when an uncaught IHealthCheck exception is converted to an Unhealthy
/// entry) — that fallback can otherwise leak hostnames/connection details onto
/// a public, unauthenticated probe (/health/live|ready|startup). A check's own
/// deliberately authored Description passed alongside an Exception (the
/// idiomatic `HealthCheckResult.Unhealthy(safeMessage, ex)` pattern — see
/// KeycloakHealthCheck) is a different, already-safe string and survives
/// compact mode unchanged. Detailed mode never suppresses `description` and
/// always includes `exception`.
/// </summary>
public static class HealthCheckJsonResponseWriter
{
    public static Task WriteCompactAsync(HttpContext context, HealthReport report) => WriteAsync(context, report, detailed: false);

    public static Task WriteDetailedAsync(HttpContext context, HealthReport report) => WriteAsync(context, report, detailed: true);

    private static Task WriteAsync(HttpContext context, HealthReport report, bool detailed)
    {
        context.Response.ContentType = "application/json";

        var entries = report.Entries.ToDictionary(e => e.Key, object (e) =>
        {
            var entry = new Dictionary<string, object?>
            {
                ["status"] = e.Value.Status.ToString(),
                ["duration"] = e.Value.Duration.ToString(),
                ["tags"] = e.Value.Tags,
                // Suppress only the framework's own exception-message-as-description
                // fallback (Description == Exception.Message) — a check's own safe,
                // hand-authored description passed alongside an exception is left as-is.
                ["description"] = detailed
                    || e.Value.Exception is null
                    || e.Value.Description != e.Value.Exception.Message
                    ? e.Value.Description
                    : null,
            };
            if (detailed)
            {
                entry["exception"] = e.Value.Exception?.Message;
            }
            return entry;
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
