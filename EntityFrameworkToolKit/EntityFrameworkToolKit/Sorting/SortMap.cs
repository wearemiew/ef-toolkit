using System.Linq.Expressions;

namespace EntityFrameworkToolKit.Sorting;

/// <summary>
/// The allow-list of fields a client may sort <typeparamref name="T"/> by, mapping public field names to key selectors.
/// Build it once (e.g. as a <see langword="static readonly"/> field) and pass it to
/// <see cref="SortQueryableExtensions.ApplySort{T}"/>.
/// </summary>
/// <remarks>
/// Register every field before the map is first used: registration is not thread-safe, while reading a fully built
/// map from many requests at once is.
/// </remarks>
/// <typeparam name="T">The type being sorted.</typeparam>
public sealed class SortMap<T>
{
    private readonly Dictionary<string, LambdaExpression> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _fields = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SortMap{T}"/> class.
    /// </summary>
    public SortMap()
    {
        Fields = _fields.AsReadOnly();
    }

    /// <summary>The field names clients may sort by, in registration order.</summary>
    public IReadOnlyList<string> Fields { get; }

    internal LambdaExpression? DefaultKey { get; private set; }

    internal bool DefaultDescending { get; private set; }

    /// <summary>
    /// Allows sorting by <paramref name="name"/> (case-insensitive) using <paramref name="key"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="name">The public field name, e.g. <c>price</c>. It must not contain commas, whitespace or a leading sign.</param>
    /// <param name="key">The key selector, translated to SQL.</param>
    /// <returns>This map, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is invalid or already registered.</exception>
    public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsWhiteSpace(c) || c == ',') || name[0] is '-' or '+')
            throw new ArgumentException($"'{name}' is not a valid sort field name.", nameof(name));
        if (!_keys.TryAdd(name, key))
            throw new ArgumentException($"Sort field '{name}' is already registered.", nameof(name));

        _fields.Add(name);
        return this;
    }

    /// <summary>
    /// Sets the key used when the client gives no sort, and appended as the final tie-breaker so pages are stable.
    /// Use a unique key such as the primary key.
    /// </summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="key">The key selector, translated to SQL.</param>
    /// <param name="descending">Whether the default order is descending.</param>
    /// <returns>This map, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A default was already set.</exception>
    public SortMap<T> Default<TKey>(Expression<Func<T, TKey>> key, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (DefaultKey is not null)
            throw new InvalidOperationException("The default sort key is already set.");

        DefaultKey = key;
        DefaultDescending = descending;
        return this;
    }

    internal bool TryGetKey(string name, out LambdaExpression key) => _keys.TryGetValue(name, out key!);
}
