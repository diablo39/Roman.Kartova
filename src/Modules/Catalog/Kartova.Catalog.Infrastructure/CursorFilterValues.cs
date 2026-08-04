namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Canonical wire-format for a multi-select filter's cursor <c>f</c>-map value (ADR-0095):
/// sorted, comma-joined, ordinal. <see cref="StringComparer.Ordinal"/> — never the
/// culture-sensitive default comparer — so two API replicas running under different cultures
/// canonicalize the same filter selection identically, and a client that supplies the same
/// values in a different order between page 1 and page 2 replays the identical f-map value
/// instead of tripping a spurious <c>CursorFilterMismatchException</c>.
/// <see cref="ListApplicationsHandler"/> and <see cref="ListServicesHandler"/> route every
/// multi-select f-map value through this instead of hand-writing the sort+join, so the two
/// cannot independently drift onto the culture-sensitive default comparer.
/// </summary>
internal static class CursorFilterValues
{
    /// <summary>Sorted (ordinal), comma-joined <c>Guid.ToString("D")</c> values.</summary>
    public static string Join(IEnumerable<Guid> values) =>
        string.Join(",", values.Select(v => v.ToString("D")).OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>Sorted (ordinal), comma-joined strings — e.g. enum names.</summary>
    public static string Join(IEnumerable<string> values) =>
        string.Join(",", values.OrderBy(s => s, StringComparer.Ordinal));
}
