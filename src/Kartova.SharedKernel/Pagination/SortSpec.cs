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
    Expression<Func<TEntity, object>> KeySelector)
{
    private Func<TEntity, object>? _compiled;

    /// <summary>
    /// Opt-in flag: <see langword="true"/> when this sort key can be <c>NULL</c> for some rows
    /// (a nullable column or expression). When set, keyset pagination applies a null-safe
    /// <c>ORDER BY</c> (NULLS LAST for ascending, NULLS FIRST for descending, encoded explicitly
    /// so PostgreSQL and the SQLite test path agree) and a null-aware boundary predicate, and the
    /// cursor can carry a <c>NULL</c> boundary key (TD-001). Default <see langword="false"/> keeps
    /// the original scalar-only predicate and provider-default ordering unchanged — required for
    /// expression selectors whose translated SQL must stay byte-identical to a partial index
    /// (e.g. the VM JSONB sort selectors), and correct for any genuinely non-null key.
    /// <para>
    /// A <see langword="false"/> spec whose key is nonetheless NULL at runtime reintroduces the
    /// silent-truncation bug — set this to <see langword="true"/> whenever the underlying key is
    /// nullable and not otherwise guaranteed non-null.
    /// </para>
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Lazily-compiled extractor for the sort key, used to read the boundary
    /// row's value during cursor encoding (post-query, in-memory). Compiled
    /// once per spec instance; production use compiles once per process at
    /// startup since specs are <c>static readonly</c> fields.
    /// </summary>
    public Func<TEntity, object> CompiledKeySelector =>
        _compiled ??= KeySelector.Compile();
}
