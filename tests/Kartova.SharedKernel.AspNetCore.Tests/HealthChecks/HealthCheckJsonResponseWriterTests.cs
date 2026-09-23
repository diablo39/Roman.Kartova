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
        Assert.AreEqual("Healthy", doc.RootElement.GetProperty("status").GetString());
        var postgres = doc.RootElement.GetProperty("entries").GetProperty("postgres");
        Assert.AreEqual(JsonValueKind.Null, postgres.GetProperty("description").ValueKind);
        Assert.IsFalse(postgres.TryGetProperty("exception", out _), "compact writer must not include an exception field");
        Assert.AreEqual("application/json", context.Response.ContentType);
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
        return new HealthReport(entries, HealthStatus.Healthy, TimeSpan.FromMilliseconds(12.8));
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
