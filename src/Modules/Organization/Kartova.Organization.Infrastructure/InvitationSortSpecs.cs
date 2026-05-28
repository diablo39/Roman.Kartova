using System.Linq.Expressions;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Per-resource sort allow-list for the Invitations list endpoint, co-located
/// with the handler that enforces it (ADR-0095 §5). Mirrors
/// <see cref="TeamSortSpecs"/>.
/// </summary>
internal static class InvitationSortSpecs
{
    /// <summary>
    /// EF-translatable primary-key selector for keyset ORDER BY / WHERE clauses.
    /// Accesses the private <c>_id</c> backing field via <see cref="EF.Property{TProperty}"/>
    /// so EF Core does not require a value-converter on the strong-typed
    /// <c>InvitationId</c> property.
    /// </summary>
    public static readonly Expression<Func<Invitation, Guid>> IdSelector =
        x => EF.Property<Guid>(x, "_id");

    public static readonly SortSpec<Invitation> InvitedAt =
        new("invitedAt", x => x.InvitedAt);

    public static readonly SortSpec<Invitation> ExpiresAt =
        new("expiresAt", x => x.ExpiresAt);

    public static readonly SortSpec<Invitation> Email =
        new("email", x => x.Email);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [InvitedAt.FieldName, ExpiresAt.FieldName, Email.FieldName];

    /// <summary>
    /// Returns an EF-translatable predicate that matches the invitation with
    /// the given id. Used by <see cref="RevokeInvitationHandler"/> so the
    /// handler never references the <c>_id</c> backing-field name directly.
    /// </summary>
    public static Expression<Func<Invitation, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, "_id") == id;

    public static SortSpec<Invitation> Resolve(InvitationSortField field) => field switch
    {
        InvitationSortField.InvitedAt => InvitedAt,
        InvitationSortField.ExpiresAt => ExpiresAt,
        InvitationSortField.Email => Email,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
