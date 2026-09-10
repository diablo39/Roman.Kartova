namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when a paginated request's <c>sortBy</c> does not match the sort field
/// encoded in the supplied cursor (the <c>sf</c> discriminator). This is a 400 Bad
/// Request — the cursor was issued under a different sort field, so applying the new
/// field's keyset predicate against the old field's boundary value would silently skip
/// or repeat rows (the two fields need only share a comparable CLR type for this to go
/// undetected). Same failure class as <see cref="CursorFilterMismatchException"/>, for
/// the sort key rather than the filters. Mapped to RFC 7807 by
/// <c>PagingExceptionHandler</c> with problem-type slug <c>cursor-sort-field-mismatch</c>.
/// ADR-0095 (amended 2026-09-10, TD-004).
/// </summary>
public sealed class CursorSortFieldMismatchException : Exception
{
    public string ExpectedField { get; }
    public string ActualField { get; }

    public CursorSortFieldMismatchException(string expectedField, string actualField)
        : base(MakeMessage(expectedField, actualField))
    {
        ExpectedField = expectedField;
        ActualField = actualField;
    }

    // Validation runs in the helper (pre-base) so the base Exception is never constructed
    // with null/empty inputs — mirrors CursorFilterMismatchException. base(...) executes
    // before the ctor body, so body guards would fire AFTER message construction.
    private static string MakeMessage(string expectedField, string actualField)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedField);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualField);
        return $"Cursor was issued for sortBy={expectedField} but request uses sortBy={actualField}.";
    }
}
