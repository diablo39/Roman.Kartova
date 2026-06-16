using System.Diagnostics.CodeAnalysis;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// Assembly-scoped singleton fixture for audit integration tests. One Postgres container
/// is created exactly once per assembly run and shared across all test classes.
///
/// Requires <c>[assembly: DoNotParallelize]</c> (see <c>Properties/AssemblyInfo.cs</c>)
/// so the single container is not started concurrently from multiple ClassInitialize calls.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public sealed class IntegrationTestAssemblySetup
{
    public static AuditLogFixture Fx { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Fx = await AuditLogFixture.CreateAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (Fx is not null)
        {
            await Fx.DisposeAsync();
        }
    }
}
