using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when <c>?sortOrder</c> is not "asc" or "desc". Mapped to RFC 7807 400
/// by <c>PagingExceptionHandler</c>. ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidSortOrderException : Exception
{
    public string Value { get; }
    public InvalidSortOrderException(string value)
        : base($"Sort order '{value}' is not allowed. Allowed: asc, desc.")
    {
        Value = value;
    }
}
