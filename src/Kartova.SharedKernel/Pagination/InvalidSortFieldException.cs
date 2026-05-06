using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when <c>?sortBy</c> falls outside the per-resource allowlist.
/// Mapped to RFC 7807 400 with <c>allowedFields</c> in the response.
/// ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidSortFieldException : Exception
{
    public string FieldName { get; }
    public IReadOnlyList<string> AllowedFields { get; }

    public InvalidSortFieldException(string fieldName, IReadOnlyList<string> allowedFields)
        : base($"Sort field '{fieldName}' is not allowed. Allowed: {string.Join(", ", allowedFields)}.")
    {
        FieldName = fieldName;
        AllowedFields = allowedFields;
    }
}
