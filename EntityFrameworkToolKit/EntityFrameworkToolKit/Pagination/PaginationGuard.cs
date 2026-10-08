using System.Linq.Expressions;

namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// Detects queries that would be paged without a defined order, which makes page contents unpredictable.
/// </summary>
internal static class PaginationGuard
{
    private static readonly HashSet<string> OrderingMethods = new()
    {
        nameof(Queryable.OrderBy),
        nameof(Queryable.OrderByDescending),
        nameof(Queryable.ThenBy),
        nameof(Queryable.ThenByDescending),
        nameof(Queryable.Order),
        nameof(Queryable.OrderDescending),
    };

    /// <summary>
    /// Returns whether the query's own operator chain contains an ordering operator.
    /// Ordering inside subqueries (e.g. in a projection) does not count.
    /// </summary>
    public static bool IsOrdered(IQueryable query)
    {
        var expression = query.Expression;
        while (expression is MethodCallExpression call)
        {
            if (call.Method.DeclaringType == typeof(Queryable) && OrderingMethods.Contains(call.Method.Name))
                return true;
            if (call.Arguments.Count == 0)
                return false;
            expression = call.Arguments[0];
        }

        return false;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="query"/> has no ordering operator.
    /// </summary>
    public static void EnsureOrdered(IQueryable query)
    {
        if (!IsOrdered(query))
            throw new InvalidOperationException(
                "Pagination requires an ordered query. Call OrderBy/OrderByDescending before paging, " +
                "otherwise the database may return rows in a different order on every page.");
    }
}
