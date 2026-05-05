using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when <c>?limit</c> falls outside the allowed range
/// [<see cref="MinLimit"/>, <see cref="MaxLimit"/>].
/// Mapped to RFC 7807 400 with <c>type = invalid-limit</c> by
/// <c>PagingExceptionHandler</c>. ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidLimitException : Exception
{
    public int Limit { get; }
    public int MinLimit { get; }
    public int MaxLimit { get; }

    public InvalidLimitException(int limit, int minLimit, int maxLimit)
        : base($"limit '{limit}' is out of range. Must be between {minLimit} and {maxLimit}.")
    {
        Limit = limit;
        MinLimit = minLimit;
        MaxLimit = maxLimit;
    }
}
