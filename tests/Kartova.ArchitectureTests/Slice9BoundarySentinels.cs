using System.IO;
using System.Linq;
using Kartova.SharedKernel;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Slice-9 §11.1 boundary sentinels — drift tests that codify the architectural
/// contracts the slice depends on. A failure here means a regression has snuck
/// past the per-task review loop.
/// </summary>
[TestClass]
public sealed class Slice9BoundarySentinels
{
    /// <summary>
    /// The <c>User</c> and <c>Invitation</c> aggregates are owned by the Organization
    /// module — Catalog and SharedKernel must not carry copies. Cross-module access
    /// goes through <c>IUserDirectory</c> (the cross-cutting port in
    /// <c>Kartova.SharedKernel.Multitenancy</c>), never via duplicated entity types.
    /// </summary>
    [TestMethod]
    public void Organization_owns_users_and_invitations_tables()
    {
        AssertExactlyOneTypeNamed("User", typeof(Kartova.Organization.Domain.User));
        AssertExactlyOneTypeNamed("Invitation", typeof(Kartova.Organization.Domain.Invitation));
    }

    private static void AssertExactlyOneTypeNamed(string simpleName, Type expectedType)
    {
        var matches = AssemblyRegistry.AllProduction()
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t.Name == simpleName)
            .ToArray();

        Assert.AreEqual(
            1,
            matches.Length,
            $"Expected exactly one production type named '{simpleName}' (owned by Organization.Domain). " +
            $"Found: {string.Join(", ", matches.Select(t => t.FullName))}");

        Assert.AreEqual(
            expectedType.FullName,
            matches[0].FullName,
            $"Type '{simpleName}' must live at '{expectedType.FullName}', not '{matches[0].FullName}'.");

        Assert.AreEqual(
            "Kartova.Organization.Domain",
            matches[0].Namespace,
            $"Type '{simpleName}' must reside in 'Kartova.Organization.Domain'.");
    }

    /// <summary>
    /// There must be exactly ONE production implementation of <see cref="IDistributedLock"/>:
    /// <c>PostgresAdvisoryLock</c> in <c>Kartova.SharedKernel.Postgres</c>. The implementation
    /// must use session-level <c>pg_try_advisory_lock</c> (auto-released on connection drop),
    /// not transaction-scoped <c>pg_try_advisory_xact_lock</c> (would deadlock leader-elected
    /// periodic services that span longer than a transaction — ADR-0099).
    /// </summary>
    [TestMethod]
    public void IDistributedLock_implementations_use_session_advisory_locks()
    {
        var implementations = AssemblyRegistry.AllProduction()
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IDistributedLock).IsAssignableFrom(t))
            .ToArray();

        Assert.AreEqual(
            1,
            implementations.Length,
            "Expected exactly one production IDistributedLock implementation. " +
            $"Found: {string.Join(", ", implementations.Select(t => t.FullName))}");

        var impl = implementations[0];
        Assert.AreEqual(
            "PostgresAdvisoryLock",
            impl.Name,
            $"The single IDistributedLock implementation must be PostgresAdvisoryLock, not '{impl.Name}'.");
        Assert.AreEqual(
            "Kartova.SharedKernel.Postgres",
            impl.Namespace,
            $"PostgresAdvisoryLock must reside in 'Kartova.SharedKernel.Postgres', not '{impl.Namespace}'.");

        // Surrogate for "uses pg_try_advisory_lock not pg_try_advisory_xact_lock". NetArchTest
        // can't introspect SQL strings, so we read the source file directly. The repo layout is
        // stable enough for this to be reliable (ADR-0082 modular monolith — one csproj per BC).
        var sourcePath = LocatePostgresAdvisoryLockSource();
        Assert.IsTrue(
            File.Exists(sourcePath),
            $"PostgresAdvisoryLock.cs source not found at expected path: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.IsTrue(
            source.Contains("pg_try_advisory_lock", StringComparison.Ordinal),
            "PostgresAdvisoryLock.cs must call session-level pg_try_advisory_lock (ADR-0099).");
        Assert.IsFalse(
            source.Contains("pg_try_advisory_xact_lock", StringComparison.Ordinal),
            "PostgresAdvisoryLock must NOT use transaction-scoped pg_try_advisory_xact_lock — " +
            "leader-elected periodic services hold the lock across many transactions (ADR-0099).");
    }

    private static string LocatePostgresAdvisoryLockSource() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Kartova.SharedKernel.Postgres", "PostgresAdvisoryLock.cs"));

    /// <summary>
    /// Slice-9 H3 surfaced that Alpine-based runtime containers without the
    /// <c>tzdata</c> package cannot resolve IANA timezone ids — every call to
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> with a non-UTC id returns null,
    /// and <c>Organization.UpdateProfile</c> rejects the request with
    /// "Unknown IANA time-zone id." → HTTP 400. The production fix is
    /// <c>apk add --no-cache tzdata</c> in <c>src/Kartova.Api/Dockerfile</c>.
    /// <para>
    /// This sentinel runs on the test host (typically Windows or Linux with tzdata)
    /// and confirms the IANA ids slice-9 advertises do resolve. It won't catch the
    /// Alpine regression by itself (CI may run on Linux with tzdata), but it makes
    /// the timezone-resolution dependency visible and would fail on a stripped-down
    /// minimal Linux runtime.
    /// </para>
    /// </summary>
    [TestMethod]
    public void Runtime_can_resolve_common_IANA_timezones()
    {
        foreach (var tz in new[] { "Europe/Warsaw", "Europe/London", "America/New_York", "UTC" })
        {
            Assert.IsTrue(
                TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _),
                $"Test host runtime cannot resolve IANA timezone '{tz}' — " +
                "production Alpine container needs tzdata package installed " +
                "(see src/Kartova.Api/Dockerfile, slice-9 H3 deviation).");
        }
    }

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
