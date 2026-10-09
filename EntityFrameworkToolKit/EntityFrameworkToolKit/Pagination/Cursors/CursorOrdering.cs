using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Pagination.Cursors;

/// <summary>
/// The ordering of a query, read from its own top-level <c>OrderBy</c>/<c>ThenBy</c> chain, split into the unordered
/// source and the sort keys so the page query can be rebuilt with a keyset filter.
/// </summary>
internal sealed class CursorOrdering<T>
{
    private static readonly HashSet<Type> SupportedTypes = new()
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
        typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
        typeof(DateOnly), typeof(TimeOnly), typeof(string), typeof(Guid),
    };

    private readonly IQueryable<T> _source; // the query before its ordering
    private readonly IReadOnlyList<MethodCallExpression> _wrappers;
    private readonly Lazy<Func<T, object?[]>> _readKeys;

    private CursorOrdering(IQueryable<T> source, IReadOnlyList<CursorKey> keys, IReadOnlyList<MethodCallExpression> wrappers)
    {
        _source = source;
        Keys = keys;
        _wrappers = wrappers;
        // Only needed when a page has items to build cursors from.
        _readKeys = new Lazy<Func<T, object?[]>>(() => CompileKeyReader(keys));
        Fingerprint = ComputeFingerprint(keys);
    }

    /// <summary>The sort keys, most significant first.</summary>
    public IReadOnlyList<CursorKey> Keys { get; }

    /// <summary>
    /// A short hash of the keys and directions, so cursors can't cross sorts. It is the same on every server running
    /// the same code; changing a sort key's expression (or a closure it captures) changes it.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Reads the ordering of <paramref name="query"/>. The ordering must be its last step, apart from EF Core
    /// operators such as <c>AsNoTracking</c> or <c>Include</c>, which are kept.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The query is unordered, its ordering isn't the last step, or a key is nullable or of an unsupported type.
    /// </exception>
    public static CursorOrdering<T> From(IQueryable<T> query)
    {
        var wrappers = new List<MethodCallExpression>();
        var expression = query.Expression;
        while (expression is MethodCallExpression wrapper && wrapper.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions))
        {
            wrappers.Add(wrapper);
            expression = wrapper.Arguments[0];
        }

        var keys = new List<CursorKey>();
        while (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable))
        {
            var descending = call.Method.Name.EndsWith("Descending", StringComparison.Ordinal);
            switch (call.Method.Name)
            {
                case nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending):
                    keys.Add(new CursorKey(Unquote(call.Arguments[1]), descending));
                    expression = call.Arguments[0];
                    continue;
                case nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending):
                    keys.Add(new CursorKey(Unquote(call.Arguments[1]), descending));
                    return Create(query.Provider, call.Arguments[0], keys, wrappers);
                case nameof(Queryable.Order) or nameof(Queryable.OrderDescending):
                    var item = Expression.Parameter(typeof(T), "x");
                    keys.Add(new CursorKey(Expression.Lambda(item, item), descending));
                    return Create(query.Provider, call.Arguments[0], keys, wrappers);
            }

            break;
        }

        PaginationGuard.EnsureOrdered(query);
        throw NotLastStep();
    }

    /// <summary>
    /// Builds the page query: the source, then <paramref name="filter"/>, then the ordering (every direction flipped
    /// when <paramref name="reverse"/> is set), then <c>Take(<paramref name="take"/>)</c>, then the EF Core operators
    /// that followed the original ordering.
    /// </summary>
    public IQueryable<T> BuildPage(Expression<Func<T, bool>>? filter, bool reverse, int take)
    {
        var page = filter is null ? _source : _source.Where(filter);
        return Rewrap(ApplyOrder(page, reverse).Take(take));
    }

    private IQueryable<T> ApplyOrder(IQueryable<T> source, bool reverse)
    {
        var expression = source.Expression;
        for (var i = 0; i < Keys.Count; i++)
        {
            var descending = Keys[i].Descending != reverse;
            var method = (i == 0, descending) switch
            {
                (true, false) => nameof(Queryable.OrderBy),
                (true, true) => nameof(Queryable.OrderByDescending),
                (false, false) => nameof(Queryable.ThenBy),
                (false, true) => nameof(Queryable.ThenByDescending),
            };
            expression = Expression.Call(
                typeof(Queryable), method, new[] { typeof(T), Keys[i].Type }, expression, Expression.Quote(Keys[i].Selector));
        }

        return source.Provider.CreateQuery<T>(expression);
    }

    // Re-applies the EF Core operators (e.g. AsNoTracking) that followed the ordering.
    private IQueryable<T> Rewrap(IQueryable<T> inner)
    {
        var expression = inner.Expression;
        for (var i = _wrappers.Count - 1; i >= 0; i--)
            expression = _wrappers[i].Update(_wrappers[i].Object, _wrappers[i].Arguments.Skip(1).Prepend(expression));
        return inner.Provider.CreateQuery<T>(expression);
    }

    /// <summary>Reads the sort key values of <paramref name="item"/>.</summary>
    /// <exception cref="InvalidOperationException">A key can't be read (e.g. an unloaded navigation) or is null.</exception>
    public object?[] ReadKeys(T item)
    {
        object?[] values;
        try
        {
            values = _readKeys.Value(item);
        }
        catch (NullReferenceException ex)
        {
            throw new InvalidOperationException(
                "A cursor sort key could not be read from a returned item. Sort keys must be readable from the items " +
                "themselves: sort by a projected value or include the navigation the key uses.", ex);
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is null)
                throw new InvalidOperationException(
                    $"The cursor sort key '{Keys[i].Selector}' returned null. Cursor pagination needs non-null sort keys.");
        }

        return values;
    }

    private static CursorOrdering<T> Create(IQueryProvider provider, Expression source, List<CursorKey> keys, List<MethodCallExpression> wrappers)
    {
        keys.Reverse();
        foreach (var key in keys)
        {
            if (key.Selector.Parameters[0].Type != typeof(T))
                throw NotLastStep();
            if (Nullable.GetUnderlyingType(key.Type) is not null || IsNullableReference(key.Selector.Body))
                throw new InvalidOperationException(
                    $"The sort key '{key.Selector}' is nullable. Cursor pagination needs non-nullable sort keys: rows " +
                    "with a NULL key would be skipped, because SQL comparisons with NULL are never true.");
            if (!key.Type.IsEnum && !SupportedTypes.Contains(key.Type))
                throw new InvalidOperationException(
                    $"The sort key '{key.Selector}' has type {key.Type.Name}, which cursor pagination does not support. " +
                    "Use numbers, strings, dates/times, Guids or enums.");
        }

        return new CursorOrdering<T>(provider.CreateQuery<T>(source), keys, wrappers);
    }

    // A property or field declared as a nullable reference (e.g. string?). Unannotated members can't be judged here;
    // ReadKeys still rejects a null it meets.
    private static bool IsNullableReference(Expression body)
    {
        if (body is not MemberExpression { Member: var member } || body.Type.IsValueType)
            return false;
        var context = new NullabilityInfoContext();
        var info = member switch
        {
            PropertyInfo property => context.Create(property),
            FieldInfo field => context.Create(field),
            _ => null,
        };
        return info?.ReadState == NullabilityState.Nullable;
    }

    private static InvalidOperationException NotLastStep() => new(
        "Cursor pagination needs the ordering to be the query's last step, so the sort keys can be read from the " +
        "returned items. Move Where/Select (and other operators) before OrderBy.");

    private static LambdaExpression Unquote(Expression expression) =>
        (LambdaExpression)(expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression);

    private static Func<T, object?[]> CompileKeyReader(IReadOnlyList<CursorKey> keys)
    {
        var item = Expression.Parameter(typeof(T), "item");
        var values = keys.Select(k => Expression.Convert(k.BodyFor(item), typeof(object)));
        return Expression.Lambda<Func<T, object?[]>>(Expression.NewArrayInit(typeof(object), values), item).Compile();
    }

    private static string ComputeFingerprint(IReadOnlyList<CursorKey> keys)
    {
        // One shared parameter name, so "e => e.Id" and "x => x.Id" give the same fingerprint.
        var item = Expression.Parameter(typeof(T), "x");
        var text = string.Join(";", keys.Select(k => k.BodyFor(item) + (k.Descending ? " desc" : " asc")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8);
    }
}
