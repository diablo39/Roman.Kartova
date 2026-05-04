using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when a cursor cannot be decoded, has been tampered with, is missing
/// required fields, or its embedded direction does not match the current
/// request's <c>sortOrder</c>. Mapped to RFC 7807 400 by
/// <c>PagingExceptionHandler</c>. ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidCursorException : Exception
{
    public InvalidCursorException(string message) : base(message) { }
    public InvalidCursorException(string message, Exception inner) : base(message, inner) { }
}
