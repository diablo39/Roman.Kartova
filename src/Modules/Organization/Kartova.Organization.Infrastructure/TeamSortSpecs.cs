using System.Linq.Expressions;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Per-resource sort allowlist for the Teams list endpoint, co-located with
/// the handler that enforces it (ADR-0095 §5). Mirrors <c>ApplicationSortSpecs</c>.
/// </summary>
internal static class TeamSortSpecs
{
    /// <summary>
    /// EF-translatable primary-key selector for keyset ORDER BY / WHERE clauses.
    /// Accesses the private <c>_id</c> backing field via <see cref="EF.Property{TProperty}"/>
    /// so EF Core does not require a value-converter on the strong-typed
    /// <see cref="TeamId"/> property.
    /// </summary>
    public static readonly Expression<Func<Team, Guid>> IdSelector =
        x => EF.Property<Guid>(x, "_id");

    public static readonly SortSpec<Team> CreatedAt =
        new("createdAt", x => x.CreatedAt);

    public static readonly SortSpec<Team> DisplayName =
        new("displayName", x => x.DisplayName);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [CreatedAt.FieldName, DisplayName.FieldName];

    /// <summary>
    /// Returns an EF-translatable predicate that matches the team with the
    /// given id. Used by <see cref="GetTeamHandler"/> so that handler never
    /// references the <c>_id</c> backing-field name directly.
    /// </summary>
    public static Expression<Func<Team, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, "_id") == id;

    public static SortSpec<Team> Resolve(TeamSortField field) => field switch
    {
        TeamSortField.CreatedAt => CreatedAt,
        TeamSortField.DisplayName => DisplayName,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
