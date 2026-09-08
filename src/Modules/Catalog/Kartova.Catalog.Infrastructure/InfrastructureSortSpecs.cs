using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for the Infrastructure/VM list endpoints (ADR-0095 §5).
/// Mirrors <see cref="ServiceSortSpecs"/> — JSONB attribute sort is deferred (slice-1
/// allowlist, see <see cref="InfrastructureSortField"/>).</summary>
internal static class InfrastructureSortSpecs
{
    public static readonly Expression<Func<InfrastructureResource, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfInfrastructureConfiguration.IdFieldName);

    /// <summary>Re-exports the shadow-PK field name for correlated sub-queries, which need
    /// <c>EF.Property</c> inline rather than a pre-built expression (mirrors
    /// <c>ServiceSortSpecs.IdFieldName</c>). EF Core does not inline user-defined static
    /// methods into an expression tree, so this MUST be a property, not a method.</summary>
    public static string IdFieldName => EfInfrastructureConfiguration.IdFieldName;

    public static readonly SortSpec<InfrastructureResource> CreatedAt = new("createdAt", x => x.CreatedAt);
    public static readonly SortSpec<InfrastructureResource> DisplayName = new("displayName", x => x.DisplayName);

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, DisplayName.FieldName];

    public static SortSpec<InfrastructureResource> Resolve(InfrastructureSortField field) => field switch
    {
        InfrastructureSortField.CreatedAt => CreatedAt,
        InfrastructureSortField.DisplayName => DisplayName,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
