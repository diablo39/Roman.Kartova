using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Shared <see cref="FakeTimeProvider"/> factory for Catalog domain tests.
/// Each test class keeps its own canonical <c>Now</c> constant (timestamps differ
/// across test files for narrative clarity) and calls <see cref="At"/> with it.
/// </summary>
internal static class TestClocks
{
    public static FakeTimeProvider At(DateTimeOffset now)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now);
        return c;
    }
}
