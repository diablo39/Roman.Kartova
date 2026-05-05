using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Per-resource sort allowlist for the Applications list endpoint, co-located
/// with the handler that enforces it (ADR-0095 §5).
/// </summary>
internal static class ApplicationSortSpecs
{
    public static readonly SortSpec<DomainApplication> CreatedAt =
        new("createdAt", x => x.CreatedAt);

    public static readonly SortSpec<DomainApplication> Name =
        new("name", x => x.Name);

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, Name.FieldName];

    public static SortSpec<DomainApplication> Resolve(ApplicationSortField field) => field switch
    {
        Contracts.ApplicationSortField.CreatedAt => CreatedAt,
        Contracts.ApplicationSortField.Name => Name,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
