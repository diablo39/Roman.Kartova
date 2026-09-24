using System.Text.Json;
using Kartova.SharedKernel.AspNetCore.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.SharedKernel.AspNetCore.Tests.HealthChecks;

[TestClass]
public class HealthCheckJsonResponseWriterTests
{
    [TestMethod]
    public async Task WriteCompactAsync_emits_valid_json_and_omits_exception_field()
    {
        var report = BuildReport();
        var context = NewHttpContext(out var body);

        await HealthCheckJsonResponseWriter.WriteCompactAsync(context, report);

        var doc = ParseBody(body);
        Assert.AreEqual("Unhealthy", doc.RootElement.GetProperty("status").GetString());
        var postgres = doc.RootElement.GetProperty("entries").GetProperty("postgres");
        Assert.AreEqual(JsonValueKind.Null, postgres.GetProperty("description").ValueKind);
        Assert.IsFalse(postgres.TryGetProperty("exception", out _), "compact writer must not include an exception field");
        Assert.AreEqual("application/json", context.Response.ContentType);
    }

    [TestMethod]
    public async Task WriteCompactAsync_preserves_a_check_authored_description_carried_alongside_an_exception()
    {
        // Mirrors KeycloakHealthCheck's real usage: HealthCheckResult.Unhealthy(safeMessage, ex) —
        // Description is a safe, hand-authored string, distinct from Exception.Message. Compact
        // mode must NOT null this out; only the framework's own Description == Exception.Message
        // fallback is suppressed (see WriteCompactAsync_nulls_description_when_framework_attached_an_exception).
        var report = BuildReport();
        var context = NewHttpContext(out var body);

        await HealthCheckJsonResponseWriter.WriteCompactAsync(context, report);

        var keycloak = ParseBody(body).RootElement.GetProperty("entries").GetProperty("keycloak");
        Assert.AreEqual("unreachable", keycloak.GetProperty("description").GetString(),
            "a check-authored description distinct from the exception's own message must survive compact mode");
    }

    [TestMethod]
    public async Task WriteDetailedAsync_includes_exception_message_when_present()
    {
        var report = BuildReport();
        var context = NewHttpContext(out var body);

        await HealthCheckJsonResponseWriter.WriteDetailedAsync(context, report);

        var doc = ParseBody(body);
        var keycloak = doc.RootElement.GetProperty("entries").GetProperty("keycloak");
        Assert.AreEqual("boom", keycloak.GetProperty("exception").GetString());
    }

    [TestMethod]
    public async Task WriteCompactAsync_nulls_description_when_framework_attached_an_exception()
    {
        const string rawExceptionText = "raw exception text: connection refused to 10.0.0.5:5432";
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["postgres"] = new HealthReportEntry(
                HealthStatus.Unhealthy, description: rawExceptionText, duration: TimeSpan.FromMilliseconds(5),
                exception: new InvalidOperationException(rawExceptionText), data: null, tags: ["ready"]),
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(5));
        var compactContext = NewHttpContext(out var compactBody);
        var detailedContext = NewHttpContext(out var detailedBody);

        await HealthCheckJsonResponseWriter.WriteCompactAsync(compactContext, report);
        await HealthCheckJsonResponseWriter.WriteDetailedAsync(detailedContext, report);

        var compactPostgres = ParseBody(compactBody).RootElement.GetProperty("entries").GetProperty("postgres");
        Assert.AreEqual(JsonValueKind.Null, compactPostgres.GetProperty("description").ValueKind,
            "compact mode must not leak the framework's exception-message-as-description fallback");

        var detailedPostgres = ParseBody(detailedBody).RootElement.GetProperty("entries").GetProperty("postgres");
        Assert.AreEqual(rawExceptionText, detailedPostgres.GetProperty("description").GetString(),
            "detailed mode must leave description unchanged");
    }

    [TestMethod]
    public async Task WriteCompactAsync_preserves_a_healthy_checks_own_description_with_no_exception()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["keycloak"] = new HealthReportEntry(
                HealthStatus.Healthy, description: "KeyCloak discovery endpoint reachable",
                duration: TimeSpan.FromMilliseconds(4), exception: null, data: null, tags: ["ready"]),
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(4));
        var context = NewHttpContext(out var body);

        await HealthCheckJsonResponseWriter.WriteCompactAsync(context, report);

        var keycloak = ParseBody(body).RootElement.GetProperty("entries").GetProperty("keycloak");
        Assert.AreEqual("KeyCloak discovery endpoint reachable", keycloak.GetProperty("description").GetString());
    }

    private static HealthReport BuildReport()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["postgres"] = new HealthReportEntry(
                HealthStatus.Healthy, description: null, duration: TimeSpan.FromMilliseconds(9.8),
                exception: null, data: null, tags: ["ready", "startup"]),
            ["keycloak"] = new HealthReportEntry(
                HealthStatus.Unhealthy, description: "unreachable", duration: TimeSpan.FromMilliseconds(3),
                exception: new InvalidOperationException("boom"), data: null, tags: ["ready", "startup"]),
        };
        return new HealthReport(entries, TimeSpan.FromMilliseconds(12.8));
    }

    private static DefaultHttpContext NewHttpContext(out MemoryStream body)
    {
        body = new MemoryStream();
        return new DefaultHttpContext { Response = { Body = body } };
    }

    private static JsonDocument ParseBody(MemoryStream body)
    {
        body.Position = 0;
        return JsonDocument.Parse(body);
    }
}
