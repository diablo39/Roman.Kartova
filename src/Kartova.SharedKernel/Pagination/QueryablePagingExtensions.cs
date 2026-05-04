using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Kartova.SharedKernel.Pagination;

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

    public static async Task<CursorPage<T>> ToCursorPagedAsync<T>(
        this IQueryable<T> source,
        SortSpec<T> sort,
        SortOrder order,
        string? cursor,
        int limit,
        Expression<Func<T, Guid>> idSelector,
        CancellationToken ct)
        where T : class
    {
        if (limit < MinLimit || limit > MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit),
                $"limit must be between {MinLimit} and {MaxLimit}.");
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
            var sortValue = sort.KeySelector.Compile().Invoke(lastKept)!;
            var id = idSelector.Compile().Invoke(lastKept);
            nextCursor = CursorCodec.Encode(NormalizeForCursor(sortValue), id, order);
        }

        return new CursorPage<T>(rows, nextCursor, PrevCursor: null);
    }

    /// <summary>
    /// Applies <c>WHERE (sortKey, id) &gt; (?, ?)</c> for asc, reversed for desc.
    /// Built as an expression tree so EF translates to a row-constructor comparison
    /// in PostgreSQL (and to a logically equivalent disjunction on sqlite during tests).
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

        Expression keyGreater = order == SortOrder.Asc
            ? Expression.GreaterThan(unwrappedKey, typedConstant)
            : Expression.LessThan(unwrappedKey, typedConstant);
        Expression keyEqual = Expression.Equal(unwrappedKey, typedConstant);
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
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    private static object ConvertCursorValue(object value, Type targetType)
    {
        if (targetType == typeof(DateTimeOffset) && value is string s)
        {
            return DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (targetType == typeof(DateTime) && value is string s2)
        {
            return DateTime.Parse(s2, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
        }
        return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static object NormalizeForCursor(object value) => value switch
    {
        DateTimeOffset dto => dto.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => value,
    };
}
