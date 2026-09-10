using System.Collections.Frozen;
using System.Linq.Expressions;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.SharedKernel.Postgres.Pagination;

/// <summary>
/// EF Core <see cref="IQueryable{T}"/> extension that applies cursor-based
/// keyset pagination per ADR-0095. Handlers compose filters, joins, and
/// projection on the queryable, then call this extension at the tail.
/// </summary>
public static class QueryablePagingExtensions
{
    public const int MinLimit = 1;
    public const int MaxLimit = 200;
    public const int DefaultLimit = 50;

    public static Task<CursorPage<T>> ToCursorPagedAsync<T>(
        this IQueryable<T> source,
        SortSpec<T> sort,
        SortOrder order,
        string? cursor,
        int limit,
        Expression<Func<T, Guid>> idSelector,
        CancellationToken ct)
        where T : class
        => source.ToCursorPagedAsync(sort, order, cursor, limit, idSelector,
            idSelector.Compile(), ct);

    /// <summary>
    /// Overload for entity types that expose their primary key via a value-converted
    /// strong-typed ID (e.g. <c>ApplicationId</c>). The <paramref name="idSelector"/>
    /// expression is used only for EF Core SQL translation (keyset ORDER BY / WHERE);
    /// <paramref name="idExtractor"/> is invoked in-memory to read the id when
    /// encoding the next-page cursor. This separation is necessary because EF Core
    /// cannot translate <c>x.StrongId.Value</c> in LINQ expressions, but the CLR
    /// compiled delegate CAN access it at runtime.
    /// <para>
    /// <paramref name="expectedFilters"/> — ADR-0095 filter-state replay. An
    /// opaque, caller-owned string→string map of the filters this request
    /// applies. The codec encodes it into the next cursor; on decode,
    /// <see cref="CursorFilterComparer"/> requires the request's map to equal the
    /// cursor's map and fails closed via <see cref="CursorFilterMismatchException"/>
    /// on any difference (added, dropped, or changed). Null/empty when the caller
    /// applies no filters, in which case `f` is omitted from the cursor and an
    /// incoming cursor that carries filter state is itself a mismatch.
    /// <para>
    /// CALLER CONTRACT: a caller that applies ANY row-set-narrowing filter (a
    /// <c>WHERE</c> beyond the always-on tenant/RLS scope) MUST include that filter
    /// here. Omitting an applied filter silently breaks keyset consistency — the
    /// cursor cannot detect the filter change, so a mid-pagination change skips or
    /// repeats rows undetected. Pass only the filters that narrow the row set.
    /// </para>
    /// </summary>
    public static async Task<CursorPage<T>> ToCursorPagedAsync<T>(
        this IQueryable<T> source,
        SortSpec<T> sort,
        SortOrder order,
        string? cursor,
        int limit,
        Expression<Func<T, Guid>> idSelector,
        Func<T, Guid> idExtractor,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? expectedFilters = null)
        where T : class
    {
        if (limit < MinLimit || limit > MaxLimit)
        {
            throw new InvalidLimitException(limit, MinLimit, MaxLimit);
        }

        IQueryable<T> q = source;

        if (cursor is not null)
        {
            var decoded = CursorCodec.Decode(cursor);
            if (decoded.Direction != order)
            {
                throw new InvalidCursorException(
                    $"Cursor was issued for direction '{decoded.Direction}' but request uses '{order}'.");
            }
            // Filter-state replay (ADR-0095): the request's filter set must equal
            // the set the cursor was issued under, or paging would skip/repeat
            // rows. Domain-agnostic — CursorFilterComparer never interprets the
            // keys; the owning handler supplies them. A difference in either
            // direction is a 400.
            var mismatch = CursorFilterComparer.FindMismatch(
                decoded.Filters, expectedFilters ?? FrozenDictionary<string, string>.Empty);
            if (mismatch is { } m)
            {
                throw new CursorFilterMismatchException(m.Name, m.Expected, m.Actual);
            }
            q = ApplyKeysetFilter(
                q, sort.KeySelector, idSelector, decoded.SortValue, decoded.Id, order, sort.IsNullable);
        }

        q = OrderForKeyset(q, sort, idSelector, order);

        var rows = await q.Take(limit + 1).ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(rows.Count - 1);
            var lastKept = rows[^1];
            // May be null for an IsNullable sort whose page boundary fell inside the NULLS block —
            // Encode then emits a null-key cursor (n:true) so the next page resumes correctly (TD-001).
            var sortValue = sort.CompiledKeySelector(lastKept);
            var id = idExtractor(lastKept);
            nextCursor = CursorCodec.Encode(
                    sortValue is null ? null : NormalizeForCursor(sortValue),
                    id,
                    order,
                    expectedFilters);
        }

        return new CursorPage<T>(rows, nextCursor, PrevCursor: null);
    }

    /// <summary>
    /// Orders the query for keyset pagination: <c>ORDER BY sortKey, id</c> (reversed for
    /// descending). For an <see cref="SortSpec{TEntity}.IsNullable"/> sort a portable null-flag
    /// key (<c>sortKey IS NULL</c>) is prepended so NULLs sort LAST for ascending / FIRST for
    /// descending on both the PostgreSQL provider and the SQLite test path — which otherwise
    /// disagree on default NULL placement (TD-001). Non-nullable sorts keep the original two-key
    /// <c>ORDER BY</c> byte-for-byte, so expression selectors matched to a partial index (the VM
    /// JSONB sorts) are unaffected.
    /// </summary>
    private static IQueryable<T> OrderForKeyset<T>(
        IQueryable<T> source,
        SortSpec<T> sort,
        Expression<Func<T, Guid>> idSelector,
        SortOrder order)
        where T : class
    {
        if (!sort.IsNullable)
        {
            return order == SortOrder.Asc
                ? source.OrderBy(sort.KeySelector).ThenBy(idSelector)
                : source.OrderByDescending(sort.KeySelector).ThenByDescending(idSelector);
        }

        var nullFlag = BuildNullFlagSelector(sort.KeySelector);
        // asc → NULLS LAST: order the flag ascending so non-null (false) precedes null (true).
        // desc → NULLS FIRST: order the flag descending so null (true) precedes non-null (false).
        return order == SortOrder.Asc
            ? source.OrderBy(nullFlag).ThenBy(sort.KeySelector).ThenBy(idSelector)
            : source.OrderByDescending(nullFlag).ThenByDescending(sort.KeySelector).ThenByDescending(idSelector);
    }

    /// <summary>
    /// Applies the keyset filter <c>WHERE sortKey &gt; @p OR (sortKey = @p AND id &gt; @p)</c>
    /// for ascending order (reversed comparators for descending). The disjunctive form is
    /// portable across the EF Core PostgreSQL provider and the sqlite test path; the
    /// row-constructor form <c>(sortKey, id) &gt; (?, ?)</c> was the original target but
    /// was dropped per design spec §14 mitigation. ADR-0095.
    /// <para>
    /// When <paramref name="sortIsNullable"/> is set, a null-aware predicate is built instead so
    /// paging never truncates at a NULL boundary — the scalar predicate above evaluates to SQL
    /// UNKNOWN (→ excluded) for any NULL key, silently dropping the trailing/leading NULLS block.
    /// The null-aware form matches the NULLS LAST (asc) / NULLS FIRST (desc) ordering of
    /// <see cref="OrderForKeyset"/> and handles a NULL boundary key
    /// (<see cref="CursorNullSortValue"/>) explicitly (TD-001).
    /// </para>
    /// </summary>
    private static IQueryable<T> ApplyKeysetFilter<T>(
        IQueryable<T> source,
        Expression<Func<T, object>> keySelector,
        Expression<Func<T, Guid>> idSelector,
        object cursorSortValue,
        Guid cursorId,
        SortOrder order,
        bool sortIsNullable)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var keyBody = ReplaceParameter(keySelector.Body, keySelector.Parameters[0], param);
        var idBody = ReplaceParameter(idSelector.Body, idSelector.Parameters[0], param);

        // keySelector returns object => boxes value types via Expression.Convert.
        // Unwrap the Convert so the comparison is on the actual underlying type.
        Expression unwrappedKey;
        Type keyType;
        if (keyBody is UnaryExpression ux && ux.NodeType == ExpressionType.Convert)
        {
            unwrappedKey = ux.Operand;
            keyType = ux.Operand.Type;
        }
        else
        {
            unwrappedKey = keyBody;
            keyType = keyBody.Type;
        }

        Expression idAfter = order == SortOrder.Asc
            ? Expression.GreaterThan(idBody, Expression.Constant(cursorId))
            : Expression.LessThan(idBody, Expression.Constant(cursorId));

        Expression predicate;
        if (sortIsNullable)
        {
            var keyIsNull = Expression.Equal(unwrappedKey, Expression.Constant(null, keyType));

            if (cursorSortValue is CursorNullSortValue)
            {
                // Boundary key is NULL — the boundary sits inside the NULLS block.
                // asc (NULLS LAST): only later NULL rows remain (id after boundary).
                // desc (NULLS FIRST): the rest of the NULL block (id after boundary), then every
                //                     non-NULL row.
                predicate = order == SortOrder.Asc
                    ? Expression.AndAlso(keyIsNull, idAfter)
                    : Expression.OrElse(
                        Expression.AndAlso(keyIsNull, idAfter),
                        Expression.Not(keyIsNull));
            }
            else
            {
                var (keyAfter, keyEqual) = BuildKeyComparisons(
                    unwrappedKey, keyType, cursorSortValue, order, nullableKey: true);
                var core = Expression.OrElse(keyAfter, Expression.AndAlso(keyEqual, idAfter));
                // asc (NULLS LAST): after a non-NULL boundary the trailing NULL block still follows.
                // desc (NULLS FIRST): NULLs already precede every non-NULL row — none remain.
                predicate = order == SortOrder.Asc
                    ? Expression.OrElse(core, keyIsNull)
                    : core;
            }
        }
        else
        {
            var (keyAfter, keyEqual) = BuildKeyComparisons(
                unwrappedKey, keyType, cursorSortValue, order, nullableKey: false);
            predicate = Expression.OrElse(keyAfter, Expression.AndAlso(keyEqual, idAfter));
        }

        var lambda = Expression.Lambda<Func<T, bool>>(predicate, param);
        return source.Where(lambda);
    }

    /// <summary>
    /// Builds the <c>(keyAfter, keyEqual)</c> comparison pair for the boundary key: <c>keyAfter</c>
    /// is <c>key &gt; @p</c> (ascending) or <c>key &lt; @p</c> (descending); <c>keyEqual</c> is
    /// <c>key = @p</c>. Strings route through the two-argument <see cref="string.Compare(string, string)"/>
    /// (the only overload both providers translate). For a nullable value-type key the typed
    /// constant is built on the underlying non-nullable type (Convert.ChangeType cannot target
    /// <see cref="Nullable{T}"/>) then boxed back to the nullable column type.
    /// </summary>
    private static (Expression KeyAfter, Expression KeyEqual) BuildKeyComparisons(
        Expression unwrappedKey, Type keyType, object cursorSortValue, SortOrder order, bool nullableKey)
    {
        var constantType = nullableKey ? (Nullable.GetUnderlyingType(keyType) ?? keyType) : keyType;
        var converted = ConvertCursorValue(cursorSortValue, constantType);
        var typedConstant = Expression.Constant(converted, keyType);

        if (keyType == typeof(string))
        {
            // Expression.GreaterThan/Equal don't work on string; use string.Compare(a, b) instead.
            // EF Core SQLite and PostgreSQL providers translate the two-argument string.Compare overload.
            // The three-argument overload with StringComparison is not translatable by either provider.
            var compareMethod = typeof(string).GetMethod(
                nameof(string.Compare),
                [typeof(string), typeof(string)])!;
            var compareCall = Expression.Call(compareMethod, unwrappedKey, typedConstant);
            var zero = Expression.Constant(0);
            var after = order == SortOrder.Asc
                ? Expression.GreaterThan(compareCall, zero)
                : Expression.LessThan(compareCall, zero);
            return (after, Expression.Equal(compareCall, zero));
        }

        var keyAfter = order == SortOrder.Asc
            ? Expression.GreaterThan(unwrappedKey, typedConstant)
            : Expression.LessThan(unwrappedKey, typedConstant);
        return (keyAfter, Expression.Equal(unwrappedKey, typedConstant));
    }

    /// <summary>
    /// Builds an <c>x =&gt; sortKey == null</c> selector from an object-returning key selector,
    /// used as the NULLS-ordering flag key. Unwraps the boxing <c>Convert</c> so the null test is
    /// against the real column type.
    /// </summary>
    private static Expression<Func<T, bool>> BuildNullFlagSelector<T>(Expression<Func<T, object>> keySelector)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var body = ReplaceParameter(keySelector.Body, keySelector.Parameters[0], param);
        var unwrapped = body is UnaryExpression { NodeType: ExpressionType.Convert } ux ? ux.Operand : body;
        var isNull = Expression.Equal(unwrapped, Expression.Constant(null, unwrapped.Type));
        return Expression.Lambda<Func<T, bool>>(isNull, param);
    }

    private static Expression ReplaceParameter(Expression body, ParameterExpression from, ParameterExpression to)
        => new ParameterReplaceVisitor(from, to).Visit(body);

    private sealed class ParameterReplaceVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;
        public ParameterReplaceVisitor(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }

        // Stryker mutation `node == _from ? _to : base.VisitParameter(node)` → always `_to`
        // is accepted as near-equivalent: in our single-parameter expression-tree replacement,
        // every ParameterExpression encountered IS `_from`, so the `else` branch is functionally
        // unreachable. ADR-0095 mutation report 2026-05-07 (slice 6, re-confirmed from slice 3).
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    private static object ConvertCursorValue(object value, Type targetType)
    {
        try
        {
            if (targetType == typeof(DateTimeOffset) && value is string s)
            {
                return DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (targetType == typeof(DateTime) && value is string s2)
            {
                return DateTime.Parse(s2, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            }
            if (targetType == typeof(Guid) && value is string s3)
            {
                return Guid.Parse(s3);
            }
            // Convert.ChangeType handles primitives that implement IConvertible (string, int, long, double, bool, etc.).
            // Types without IConvertible (Guid, DateTimeOffset, custom value types) need explicit cases above.
            return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture)!;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidCursorException(
                $"Cursor sort value '{value}' is not compatible with expected type {targetType.Name}.", ex);
        }
    }

    private static object NormalizeForCursor(object value) => value switch
    {
        DateTimeOffset dto => dto.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => value,
    };
}
