using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Per-resource sort allowlist for the Applications list endpoint, co-located
/// with the handler that enforces it (ADR-0095 §5).
/// </summary>
internal static class ApplicationSortSpecs
{
    /// <summary>
    /// EF-translatable primary-key selector for keyset ORDER BY / WHERE clauses.
    /// Accesses the private <c>_id</c> backing field via <see cref="EF.Property{TProperty}"/>
    /// so EF Core does not require a value-converter on the strong-typed
    /// <c>ApplicationId</c> property. This is the single canonical reference
    /// to <see cref="EfApplicationConfiguration.IdFieldName"/> outside the EF
    /// configuration itself — handlers use this expression rather than
    /// repeating the magic string.
    /// </summary>
    public static readonly Expression<Func<DomainApplication, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfApplicationConfiguration.IdFieldName);

    public static readonly SortSpec<DomainApplication> CreatedAt =
        new("createdAt", x => x.CreatedAt);

    public static readonly SortSpec<DomainApplication> Name =
        new("name", x => x.Name);

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, Name.FieldName];

    /// <summary>
    /// Returns an EF-translatable predicate that matches the application with the
    /// given id. Used by <see cref="GetApplicationByIdHandler"/> so that handler
    /// never references <see cref="EfApplicationConfiguration.IdFieldName"/> directly.
    /// </summary>
    public static Expression<Func<DomainApplication, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfApplicationConfiguration.IdFieldName) == id;

    public static SortSpec<DomainApplication> Resolve(ApplicationSortField field) => field switch
    {
        Contracts.ApplicationSortField.CreatedAt => CreatedAt,
        Contracts.ApplicationSortField.Name => Name,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
