using System.Linq.Expressions;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Describes one sortable field for a list query: the public field name
/// (matches OpenAPI enum value) and the EF Core key selector. Per-resource
/// allowlists are expressed as collections of <c>SortSpec&lt;TEntity&gt;</c>
/// instances co-located with the handler that enforces them. ADR-0095 §5.
/// </summary>
public sealed record SortSpec<TEntity>(
    string FieldName,
    Expression<Func<TEntity, object>> KeySelector);
