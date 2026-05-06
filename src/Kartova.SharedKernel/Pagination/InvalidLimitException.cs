using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when <c>?limit</c> is outside the allowed range
/// [<see cref="MinLimit"/>, <see cref="MaxLimit"/>] OR cannot be parsed as an integer.
/// Mapped to RFC 7807 400 with <c>type = invalid-limit</c> by
/// <c>PagingExceptionHandler</c>. ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidLimitException : Exception
{
    /// <summary>Parsed numeric value, or <c>0</c> when input could not be parsed (see <see cref="RawLimit"/>).</summary>
    public int Limit { get; }

    /// <summary>The raw <c>?limit</c> value as sent by the client. Always populated.</summary>
    public string RawLimit { get; }

    public int MinLimit { get; }
    public int MaxLimit { get; }

    public InvalidLimitException(int limit, int minLimit, int maxLimit)
        : base($"limit '{limit}' is out of range. Must be between {minLimit} and {maxLimit}.")
    {
        Limit = limit;
        RawLimit = limit.ToString(CultureInfo.InvariantCulture);
        MinLimit = minLimit;
        MaxLimit = maxLimit;
    }

    public InvalidLimitException(string rawLimit, int minLimit, int maxLimit)
        : base($"limit '{rawLimit}' is not a valid integer. Must be between {minLimit} and {maxLimit}.")
    {
        Limit = 0;
        RawLimit = rawLimit;
        MinLimit = minLimit;
        MaxLimit = maxLimit;
    }
}
