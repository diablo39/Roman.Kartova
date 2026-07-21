using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainSystem = Kartova.Catalog.Domain.CatalogSystem;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for the Systems list endpoint (ADR-0095 §5).
/// Sortable on <c>displayName</c> (default asc) and <c>createdAt</c> (design §5).</summary>
internal static class SystemSortSpecs
{
    public static readonly Expression<Func<DomainSystem, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfSystemConfiguration.IdFieldName);

    public static readonly SortSpec<DomainSystem> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<DomainSystem> CreatedAt = new("createdAt", x => x.CreatedAt);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [DisplayName.FieldName, CreatedAt.FieldName];

    public static Expression<Func<DomainSystem, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfSystemConfiguration.IdFieldName) == id;

    public static SortSpec<DomainSystem> Resolve(SystemSortField field) => field switch
    {
        SystemSortField.DisplayName => DisplayName,
        SystemSortField.CreatedAt => CreatedAt,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
