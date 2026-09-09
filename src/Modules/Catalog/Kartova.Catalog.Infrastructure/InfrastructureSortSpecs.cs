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

    /// <summary>Typed smallint column (not JSONB) — not covered by the JSONB-attribute-sort
    /// deferral noted on <see cref="InfrastructureSortField"/>; keyset-safe like any other
    /// scalar column.</summary>
    public static readonly SortSpec<InfrastructureResource> Type = new("type", x => x.Type);

    /// <summary>Typed nullable varchar column (slice 2a, ADR-0115). COALESCE-d to "" (gate-8
    /// review finding, fix round 1) so the nullable column's sort key is never NULL — a NULL
    /// boundary value makes the shared keyset predicate evaluate to SQL UNKNOWN and silently
    /// truncates pagination. See <see cref="VmSortSpecs.Provider"/> for the full rationale;
    /// both tiers must agree so a row sorts the same way whether read through the generic
    /// Infrastructure list or the VM-specific list.</summary>
    public static readonly SortSpec<InfrastructureResource> Provider = new("provider", x => x.Provider ?? "");

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [CreatedAt.FieldName, DisplayName.FieldName, Type.FieldName, Provider.FieldName];

    public static SortSpec<InfrastructureResource> Resolve(InfrastructureSortField field) => field switch
    {
        InfrastructureSortField.CreatedAt => CreatedAt,
        InfrastructureSortField.DisplayName => DisplayName,
        InfrastructureSortField.Type => Type,
        InfrastructureSortField.Provider => Provider,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
