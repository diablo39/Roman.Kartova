namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Compares the filter state a cursor was issued under against the filter state
/// of the current request (ADR-0095). Domain-agnostic: it never interprets
/// filter names or values — the owning module (e.g. Catalog's
/// <c>ListApplicationsHandler</c>) supplies the key/value pairs. A difference in
/// either direction (added, dropped, or changed) means paging would skip or
/// repeat rows, so the caller raises a 400.
/// </summary>
public static class CursorFilterComparer
{
    private const string Absent = "(none)";

    /// <summary>
    /// Returns the first filter-set difference as (Name, Expected, Actual), or
    /// <c>null</c> when the two sets are equal. <c>Expected</c> is the value the
    /// cursor was issued under; <c>Actual</c> is the current request value. Keys
    /// are walked in ordinal-sorted order so the reported difference is
    /// deterministic regardless of dictionary iteration order. A key present on
    /// only one side reports <c>"(none)"</c> for the missing side. Key and value
    /// comparison is ordinal.
    /// </summary>
    public static (string Name, string Expected, string Actual)? FindMismatch(
        IReadOnlyDictionary<string, string> cursorFilters,
        IReadOnlyDictionary<string, string> requestFilters)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in cursorFilters.Keys) keys.Add(k);
        foreach (var k in requestFilters.Keys) keys.Add(k);

        foreach (var key in keys)
        {
            var inCursor = cursorFilters.TryGetValue(key, out var cursorValue);
            var inRequest = requestFilters.TryGetValue(key, out var requestValue);

            if (inCursor && inRequest)
            {
                if (!string.Equals(cursorValue, requestValue, StringComparison.Ordinal))
                {
                    return (key, cursorValue!, requestValue!);
                }
            }
            else if (inCursor)
            {
                return (key, cursorValue!, Absent);
            }
            else
            {
                return (key, Absent, requestValue!);
            }
        }

        return null;
    }
}
