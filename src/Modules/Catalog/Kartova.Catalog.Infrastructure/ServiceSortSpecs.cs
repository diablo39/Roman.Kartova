using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainService = Kartova.Catalog.Domain.Service;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for the Services list endpoint (ADR-0095 §5).</summary>
internal static class ServiceSortSpecs
{
    public static readonly Expression<Func<DomainService, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfServiceConfiguration.IdFieldName);

    public static readonly SortSpec<DomainService> CreatedAt = new("createdAt", x => x.CreatedAt);
    public static readonly SortSpec<DomainService> DisplayName = new("displayName", x => x.DisplayName);

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, DisplayName.FieldName];

    public static Expression<Func<DomainService, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfServiceConfiguration.IdFieldName) == id;

    public static SortSpec<DomainService> Resolve(ServiceSortField field) => field switch
    {
        ServiceSortField.CreatedAt => CreatedAt,
        ServiceSortField.DisplayName => DisplayName,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
