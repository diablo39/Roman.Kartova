using System.Linq.Expressions;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Per-resource sort allowlist for <c>GET /api/v1/organizations/users</c>
/// (members directory), co-located with the handler that enforces it
/// (ADR-0095 §5). <see cref="User.Id"/> is a plain <see cref="Guid"/> — no
/// strong-typed value object, so no <c>EF.Property</c> workaround needed.
/// </summary>
internal static class MemberSortSpecs
{
    /// <summary>
    /// EF-translatable primary-key selector. <see cref="User.Id"/> is a plain
    /// <see cref="Guid"/> mapped directly — no backing field indirection needed
    /// (contrast with <see cref="TeamSortSpecs.IdSelector"/>).
    /// </summary>
    public static readonly Expression<Func<User, Guid>> IdSelector = x => x.Id;

    /// <summary>
    /// In-memory id extractor for cursor encoding. Identical to the compiled
    /// form of <see cref="IdSelector"/> — kept as a separate field to mirror
    /// the dual-expression contract expected by
    /// <c>ToCursorPagedAsync(idSelector, idExtractor, ...)</c>.
    /// </summary>
    public static readonly Func<User, Guid> IdExtractor = x => x.Id;

    public static readonly SortSpec<User> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<User> Role        = new("role",        x => x.RealmRole);
    public static readonly SortSpec<User> CreatedAt   = new("createdAt",   x => x.CreatedAt);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [DisplayName.FieldName, Role.FieldName, CreatedAt.FieldName];

    public static SortSpec<User> Resolve(MemberSortField field) => field switch
    {
        MemberSortField.DisplayName => DisplayName,
        MemberSortField.Role        => Role,
        MemberSortField.CreatedAt   => CreatedAt,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
