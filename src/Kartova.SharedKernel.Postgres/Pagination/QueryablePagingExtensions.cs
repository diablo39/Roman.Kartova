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
            q = ApplyKeysetFilter(q, sort.KeySelector, idSelector, decoded.SortValue, decoded.Id, order);
        }

        q = order == SortOrder.Asc
            ? q.OrderBy(sort.KeySelector).ThenBy(idSelector)
            : q.OrderByDescending(sort.KeySelector).ThenByDescending(idSelector);

        var rows = await q.Take(limit + 1).ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(rows.Count - 1);
            var lastKept = rows[^1];
            var sortValue = sort.CompiledKeySelector(lastKept)!;
            var id = idExtractor(lastKept);
            nextCursor = CursorCodec.Encode(
                    NormalizeForCursor(sortValue),
                    id,
                    order,
                    expectedFilters);
        }

        return new CursorPage<T>(rows, nextCursor, PrevCursor: null);
    }

    /// <summary>
    /// Applies the keyset filter <c>WHERE sortKey &gt; @p OR (sortKey = @p AND id &gt; @p)</c>
    /// for ascending order (reversed comparators for descending). The disjunctive form is
    /// portable across the EF Core PostgreSQL provider and the sqlite test path; the
    /// row-constructor form <c>(sortKey, id) &gt; (?, ?)</c> was the original target but
    /// was dropped per design spec §14 mitigation. ADR-0095.
    /// </summary>
    private static IQueryable<T> ApplyKeysetFilter<T>(
        IQueryable<T> source,
        Expression<Func<T, object>> keySelector,
        Expression<Func<T, Guid>> idSelector,
        object cursorSortValue,
        Guid cursorId,
        SortOrder order)
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

        var typedConstant = Expression.Constant(ConvertCursorValue(cursorSortValue, keyType), keyType);

        Expression keyGreater;
        Expression keyEqual;
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
            keyGreater = order == SortOrder.Asc
                ? Expression.GreaterThan(compareCall, zero)
                : Expression.LessThan(compareCall, zero);
            keyEqual = Expression.Equal(compareCall, zero);
        }
        else
        {
            keyGreater = order == SortOrder.Asc
                ? Expression.GreaterThan(unwrappedKey, typedConstant)
                : Expression.LessThan(unwrappedKey, typedConstant);
            keyEqual = Expression.Equal(unwrappedKey, typedConstant);
        }
        Expression idGreater = order == SortOrder.Asc
            ? Expression.GreaterThan(idBody, Expression.Constant(cursorId))
            : Expression.LessThan(idBody, Expression.Constant(cursorId));

        var disjunction = Expression.OrElse(keyGreater, Expression.AndAlso(keyEqual, idGreater));
        var lambda = Expression.Lambda<Func<T, bool>>(disjunction, param);
        return source.Where(lambda);
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
