using System.Globalization;
using Kartova.SharedKernel.Pagination;

namespace Kartova.SharedKernel.Postgres.Pagination;

/// <summary>
/// Shared parsing helper for the wire envelope every cursor-list endpoint binds:
/// <c>?sortBy=&amp;sortOrder=&amp;limit=</c> as raw <c>string?</c> values (ADR-0095 §4.3).
/// Raw strings are required because the framework's int parser converts non-integer
/// <c>limit</c> values to the generic 400 instead of the per-resource
/// <see cref="InvalidLimitException"/> envelope, and because the enum parsers must
/// be case-insensitive to accept camelCase wire names (<c>createdAt</c>) against
/// PascalCase enum members (<c>CreatedAt</c>).
/// <para>
/// This helper centralizes the parsing that previously lived inline in three
/// endpoint delegates (Catalog applications, Organization teams, Organization
/// invitations) — same three exceptions, same paging-defaults source-of-truth
/// (<see cref="QueryablePagingExtensions"/>).
/// </para>
/// </summary>
public static class CursorListBinding
{
    /// <summary>
    /// Parses the three cursor-list query parameters into strongly-typed values.
    /// <list type="bullet">
    ///   <item><c>sortBy</c> is rejected as <see cref="InvalidSortFieldException"/>
    ///     when it falls outside the supplied <paramref name="allowedSortFields"/>
    ///     (or when it parses to an undefined enum value — the
    ///     <see cref="Enum.IsDefined{TEnum}(TEnum)"/> guard rejects numeric strings
    ///     like <c>"999"</c> that <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    ///     would otherwise accept).</item>
    ///   <item><c>sortOrder</c> is rejected as <see cref="InvalidSortOrderException"/>
    ///     under the same rules.</item>
    ///   <item><c>limit</c> defaults to <see cref="QueryablePagingExtensions.DefaultLimit"/>
    ///     when null; non-integer strings throw <see cref="InvalidLimitException"/>.
    ///     Range validation (<c>MinLimit..MaxLimit</c>) is deferred to
    ///     <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/> so this helper
    ///     stays a pure binding step.</item>
    /// </list>
    /// </summary>
    public static (TSortField? SortBy, SortOrder? SortOrder, int Limit) Bind<TSortField>(
        string? sortBy,
        string? sortOrder,
        string? limit,
        IReadOnlyList<string> allowedSortFields)
        where TSortField : struct, Enum
    {
        TSortField? parsedSortBy = null;
        if (sortBy is not null)
        {
            if (!Enum.TryParse<TSortField>(sortBy, ignoreCase: true, out var sf)
                || !Enum.IsDefined(sf))
            {
                throw new InvalidSortFieldException(sortBy, allowedSortFields);
            }
            parsedSortBy = sf;
        }

        SortOrder? parsedSortOrder = null;
        if (sortOrder is not null)
        {
            if (!Enum.TryParse<SortOrder>(sortOrder, ignoreCase: true, out var so)
                || !Enum.IsDefined(so))
            {
                throw new InvalidSortOrderException(sortOrder);
            }
            parsedSortOrder = so;
        }

        int effectiveLimit;
        if (limit is null)
        {
            effectiveLimit = QueryablePagingExtensions.DefaultLimit;
        }
        else if (!int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out effectiveLimit))
        {
            throw new InvalidLimitException(
                limit,
                QueryablePagingExtensions.MinLimit,
                QueryablePagingExtensions.MaxLimit);
        }

        return (parsedSortBy, parsedSortOrder, effectiveLimit);
    }
}
