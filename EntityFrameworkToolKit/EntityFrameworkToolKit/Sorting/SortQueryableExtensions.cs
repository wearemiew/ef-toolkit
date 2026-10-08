using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkToolKit.Sorting;

/// <summary>
/// Extension methods that sort an <see cref="IQueryable{T}"/> by a client-supplied, allow-listed sort specification.
/// </summary>
public static class SortQueryableExtensions
{
    /// <summary>
    /// Orders the query by <paramref name="sort"/> (e.g. <c>-price,name</c>: comma-separated field names, a leading
    /// <c>-</c> for descending), accepting only fields registered in <paramref name="map"/>. The map's default key is
    /// used when <paramref name="sort"/> is empty and is always appended as the final tie-breaker.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query to order. Any existing ordering is replaced.</param>
    /// <param name="sort">The client's sort specification, or <see langword="null"/> for the default order.</param>
    /// <param name="map">The fields clients may sort by.</param>
    /// <returns>The ordered query.</returns>
    /// <exception cref="InvalidQueryRequestException"><paramref name="sort"/> names an unknown field, repeats a field or has an empty segment.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="map"/> has no default key.</exception>
    public static IOrderedQueryable<T> ApplySort<T>(this IQueryable<T> query, string? sort, SortMap<T> map)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(map);
        var defaultKey = map.DefaultKey
            ?? throw new InvalidOperationException("The sort map needs a default key (SortMap.Default) to give pages a stable order.");

        var keys = Parse(sort, map);
        if (!keys.Any(k => ExpressionEqualityComparer.Instance.Equals(k.Key, defaultKey)))
            keys.Add((defaultKey, map.DefaultDescending));

        var expression = query.Expression;
        for (var i = 0; i < keys.Count; i++)
        {
            var (key, descending) = keys[i];
            var method = (i == 0, descending) switch
            {
                (true, false) => nameof(Queryable.OrderBy),
                (true, true) => nameof(Queryable.OrderByDescending),
                (false, false) => nameof(Queryable.ThenBy),
                (false, true) => nameof(Queryable.ThenByDescending),
            };
            expression = Expression.Call(
                typeof(Queryable), method, new[] { typeof(T), key.ReturnType }, expression, Expression.Quote(key));
        }

        return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(expression);
    }

    private static List<(LambdaExpression Key, bool Descending)> Parse<T>(string? sort, SortMap<T> map)
    {
        var keys = new List<(LambdaExpression, bool)>();
        if (string.IsNullOrWhiteSpace(sort))
            return keys;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawSegment in sort.Split(','))
        {
            var segment = rawSegment.Trim();
            var descending = segment.StartsWith('-');
            // Strip exactly one sign, so "--price" leaves "-price" and fails the allow-list.
            var name = segment.Length > 0 && segment[0] is '-' or '+' ? segment[1..].Trim() : segment;

            if (name.Length == 0)
                throw new InvalidQueryRequestException($"The sort '{sort}' has an empty field.", nameof(sort));
            if (!map.TryGetKey(name, out var key))
                throw new InvalidQueryRequestException(
                    $"Cannot sort by '{name}'. Allowed fields: {string.Join(", ", map.Fields)}.", nameof(sort));
            if (!seen.Add(name))
                throw new InvalidQueryRequestException($"The sort '{sort}' uses '{name}' more than once.", nameof(sort));

            keys.Add((key, descending));
        }

        return keys;
    }
}
