using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainApi = Kartova.Catalog.Domain.Api;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for the APIs list endpoint (ADR-0095 §5).
/// Sortable on every displayed column (spec §3 #14).</summary>
internal static class ApiSortSpecs
{
    public static readonly Expression<Func<DomainApi, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfApiConfiguration.IdFieldName);

    public static readonly SortSpec<DomainApi> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<DomainApi> Style = new("style", x => x.Style);
    public static readonly SortSpec<DomainApi> Version = new("version", x => x.Version);
    public static readonly SortSpec<DomainApi> CreatedAt = new("createdAt", x => x.CreatedAt);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [DisplayName.FieldName, Style.FieldName, Version.FieldName, CreatedAt.FieldName];

    public static Expression<Func<DomainApi, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfApiConfiguration.IdFieldName) == id;

    public static SortSpec<DomainApi> Resolve(ApiSortField field) => field switch
    {
        ApiSortField.DisplayName => DisplayName,
        ApiSortField.Style => Style,
        ApiSortField.Version => Version,
        ApiSortField.CreatedAt => CreatedAt,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
