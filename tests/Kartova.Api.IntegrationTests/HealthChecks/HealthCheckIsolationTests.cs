using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.Api.IntegrationTests.HealthChecks;

/// <summary>
/// Proves the ADR-0060 tag-isolation contract with a real (unreachable) dependency, not
/// just a structurally-absent one: a "ready"-tagged Postgres check that can never connect
/// must fail /health/ready while a "live"-tagged self check stays healthy — true isolation,
/// not merely "the live tag set happens not to include Postgres". Builds its own tiny
/// HealthCheckService (mirroring ModuleMigrationsHealthCheckTests.RunCheckAsync) rather than
/// touching the assembly-shared Postgres/KeyCloak containers, so it needs no container and
/// cannot affect other test classes sharing those containers.
/// </summary>
[TestClass]
public class HealthCheckIsolationTests
{
    // Port 1 is a privileged, essentially-never-listening port: connection attempts to it
    // fail immediately (connection refused) without needing a real container.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=nonexistent;Username=x;Password=x;Timeout=1";

    [TestMethod]
    public async Task Ready_check_goes_unhealthy_when_its_dependency_is_actually_down()
    {
        var report = await RunCheckAsync(registration => registration.Tags.Contains("ready"));

        Assert.AreEqual(HealthStatus.Unhealthy, report.Status);
    }

    [TestMethod]
    public async Task Live_check_stays_healthy_even_though_an_unrelated_ready_dependency_is_down()
    {
        var report = await RunCheckAsync(registration => registration.Tags.Contains("live"));

        Assert.AreEqual(HealthStatus.Healthy, report.Status);
        Assert.IsFalse(report.Entries.ContainsKey("postgres"),
            "the live-tagged report must not even include the down ready-tagged dependency");
    }

    private static async Task<HealthReport> RunCheckAsync(Func<HealthCheckRegistration, bool> predicate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddNpgSql(UnreachableConnectionString, name: "postgres", tags: ["ready"]);

        await using var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();
        return await healthCheckService.CheckHealthAsync(predicate);
    }
}
