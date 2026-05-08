namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when a paginated request's filter parameters do not match the filter
/// state encoded in the supplied cursor. This is a 400 Bad Request — the cursor
/// was issued under a different filter, so paging would silently skip rows or
/// repeat them. Mapped to RFC 7807 by <c>PagingExceptionHandler</c> with
/// problem-type slug <c>cursor-filter-mismatch</c>. ADR-0095 / ADR-0073, slice 6.
/// </summary>
public sealed class CursorFilterMismatchException : Exception
{
    public string FilterName { get; }
    public string ExpectedValue { get; }
    public string ActualValue { get; }

    public CursorFilterMismatchException(string filterName, string expectedValue, string actualValue)
        : base(MakeMessage(filterName, expectedValue, actualValue))
    {
        FilterName = filterName;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
    }

    // Validation runs in the helper (pre-base) so the base Exception is never constructed
    // with null/empty inputs — even momentarily. Order matters because base(...) executes
    // before the ctor body, so guards in the body would fire AFTER message construction.
    private static string MakeMessage(string filterName, string expectedValue, string actualValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualValue);
        return $"Cursor was issued for {filterName}={expectedValue} but request uses {filterName}={actualValue}.";
    }
}
