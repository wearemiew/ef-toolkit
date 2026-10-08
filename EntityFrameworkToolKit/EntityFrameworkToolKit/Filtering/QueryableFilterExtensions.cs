using System.Linq.Expressions;

namespace EntityFrameworkToolKit.Filtering;

/// <summary>
/// Extension methods for composing optional filters on an <see cref="IQueryable{T}"/>.
/// </summary>
public static class QueryableFilterExtensions
{
    /// <summary>
    /// Applies <paramref name="predicate"/> only when <paramref name="condition"/> is <see langword="true"/>,
    /// e.g. <c>.WhereIf(search is not null, p =&gt; p.Name.Contains(search!))</c>.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query to filter.</param>
    /// <param name="condition">Whether to apply the filter.</param>
    /// <param name="predicate">The filter, translated to SQL.</param>
    /// <returns>The filtered query, or <paramref name="query"/> unchanged.</returns>
    public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(predicate);
        return condition ? query.Where(predicate) : query;
    }
}
