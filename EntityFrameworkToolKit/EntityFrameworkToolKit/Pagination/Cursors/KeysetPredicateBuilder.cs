using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EntityFrameworkToolKit.Pagination.Cursors;

/// <summary>
/// Builds the keyset filter "rows after (or before) this position":
/// <c>k1 ▷= v1 AND ((k1 ▷ v1) OR (k1 = v1 AND k2 ▷ v2) OR …)</c>, where ▷ follows each key's direction.
/// </summary>
/// <remarks>
/// The leading <c>k1 ▷= v1</c> is implied by the rest, but it lets the database seek an index on the first key
/// instead of scanning: optimizers rarely derive a range from the OR expansion alone.
/// </remarks>
internal static class KeysetPredicateBuilder
{
    private static readonly MethodInfo StringCompare =
        typeof(string).GetMethod(nameof(string.Compare), new[] { typeof(string), typeof(string) })!;

    /// <summary>
    /// Builds the filter for the rows after <paramref name="values"/> in the order of <paramref name="keys"/>,
    /// or before them when <paramref name="backward"/> is set. Values are captured so EF Core sends them as parameters.
    /// </summary>
    public static Expression<Func<T, bool>> Build<T>(IReadOnlyList<CursorKey> keys, IReadOnlyList<object?> values, bool backward)
    {
        var item = Expression.Parameter(typeof(T), "x");
        var selectors = keys.Select(k => k.BodyFor(item)).ToList();
        var parameters = keys.Select((k, i) => Parameter(k.Type, values[i])).ToList();

        Expression? predicate = null;
        for (var i = 0; i < keys.Count; i++)
        {
            Expression term = Compare(selectors[i], parameters[i], Direction(keys[i], backward), orEqual: false);
            for (var j = i - 1; j >= 0; j--)
                term = Expression.AndAlso(Equal(selectors[j], parameters[j]), term);
            predicate = predicate is null ? term : Expression.OrElse(predicate, term);
        }

        if (keys.Count > 1)
            predicate = Expression.AndAlso(Compare(selectors[0], parameters[0], Direction(keys[0], backward), orEqual: true), predicate!);

        return Expression.Lambda<Func<T, bool>>(predicate!, item);
    }

    // A field of a captured object, not a constant, so EF Core parameterizes the value instead of inlining it.
    private static Expression Parameter(Type type, object? value)
    {
        var box = Activator.CreateInstance(typeof(StrongBox<>).MakeGenericType(type), value)!;
        return Expression.Field(Expression.Constant(box), "Value");
    }

    // Ascending keys continue upwards; descending keys, or reading backwards, continue downwards.
    private static bool Direction(CursorKey key, bool backward) => key.Descending == backward;

    private static Expression Compare(Expression left, Expression right, bool greater, bool orEqual)
    {
        if (left.Type == typeof(string) || left.Type == typeof(Guid))
        {
            Expression comparison = left.Type == typeof(string)
                ? Expression.Call(StringCompare, left, right)
                : Expression.Call(left, nameof(Guid.CompareTo), Type.EmptyTypes, right);
            return Operator(comparison, Expression.Constant(0), greater, orEqual);
        }

        (left, right) = AsComparable(left, right);
        return Operator(left, right, greater, orEqual);
    }

    private static Expression Operator(Expression left, Expression right, bool greater, bool orEqual) => (greater, orEqual) switch
    {
        (true, false) => Expression.GreaterThan(left, right),
        (true, true) => Expression.GreaterThanOrEqual(left, right),
        (false, false) => Expression.LessThan(left, right),
        (false, true) => Expression.LessThanOrEqual(left, right),
    };

    private static Expression Equal(Expression left, Expression right)
    {
        (left, right) = AsComparable(left, right);
        return Expression.Equal(left, right);
    }

    // Enums have no comparison operators in expression trees; compare their underlying values.
    private static (Expression Left, Expression Right) AsComparable(Expression left, Expression right)
    {
        if (!left.Type.IsEnum)
            return (left, right);
        var underlying = Enum.GetUnderlyingType(left.Type);
        return (Expression.Convert(left, underlying), Expression.Convert(right, underlying));
    }
}
