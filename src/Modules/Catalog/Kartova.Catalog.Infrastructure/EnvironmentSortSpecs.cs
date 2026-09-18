using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Sort allowlist for GET /catalog/environments (ADR-0095). Typed columns only; the id
/// tiebreaker is appended by the keyset pager so paging stays deterministic. <see cref="Region"/>
/// is a nullable varchar column, so it is marked <see cref="SortSpec{TEntity}.IsNullable"/> (TD-001)
/// rather than COALESCE-workaround, mirroring the current <see cref="InfrastructureSortSpecs.Provider"/>
/// pattern.</summary>
internal static class EnvironmentSortSpecs
{
    public static readonly Expression<Func<CatalogEnvironment, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfEnvironmentConfiguration.IdFieldName);

    public static string IdFieldName => EfEnvironmentConfiguration.IdFieldName;

    public static Expression<Func<CatalogEnvironment, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfEnvironmentConfiguration.IdFieldName) == id;

    public static readonly SortSpec<CatalogEnvironment> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<CatalogEnvironment> CreatedAt = new("createdAt", x => x.CreatedAt);

    // Typed smallint column (not JSONB) — boxes fine as the SortSpec<T> key selector returns
    // object; mirrors InfrastructureSortSpecs.Type (an enum column sorted the same way).
    public static readonly SortSpec<CatalogEnvironment> Type = new("type", x => x.Type);

    // Nullable varchar column — IsNullable=true so the shared keyset mechanism sorts NULLs LAST
    // (asc) / FIRST (desc) and pages through them correctly. Mirrors InfrastructureSortSpecs.Provider
    // (TD-001) rather than the superseded `?? ""` COALESCE workaround.
    public static readonly SortSpec<CatalogEnvironment> Region =
        new("region", x => x.Region!) { IsNullable = true };

    public static readonly IReadOnlyList<string> AllowedFieldNames =
    [
        DisplayName.FieldName, CreatedAt.FieldName, Type.FieldName, Region.FieldName,
    ];

    public static SortSpec<CatalogEnvironment> Resolve(EnvironmentSortField field) => field switch
    {
        EnvironmentSortField.DisplayName => DisplayName,
        EnvironmentSortField.CreatedAt => CreatedAt,
        EnvironmentSortField.Type => Type,
        EnvironmentSortField.Region => Region,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
