using Kartova.Organization.Domain;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Shared case-insensitive infix text-search predicate over a user's DisplayName and Email,
/// used by both the typeahead (<see cref="UserQueries.SearchAsync"/>) and the members directory
/// (<see cref="ListMembersHandler"/>) so the searched columns + provider workaround are defined
/// once.
/// </summary>
/// <remarks>
/// Uses <c>string.ToLower().Contains(...)</c> rather than <c>EF.Functions.ILike</c> so both the
/// Npgsql provider (which translates to <c>LOWER(...) LIKE</c>) and the InMemory provider used by
/// unit tests execute the predicate. <c>ILike</c> is Npgsql-only and throws on InMemory.
/// </remarks>
internal static class UserSearch
{
    /// <param name="source">The (already tenant-scoped) user query.</param>
    /// <param name="lowered">The search term, already lower-cased by the caller.</param>
    public static IQueryable<User> WhereTextMatches(this IQueryable<User> source, string lowered) =>
        source.Where(u => u.DisplayName.ToLower().Contains(lowered)
                       || u.Email.ToLower().Contains(lowered));
}
