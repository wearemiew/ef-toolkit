namespace EntityFrameworkToolKit;

/// <summary>
/// Thrown by the <c>…OrNotFoundAsync</c> helpers when the requested entity doesn't exist. Map it to a 404 response;
/// the message is safe to return to the client.
/// </summary>
public sealed class EntityNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityNotFoundException"/> class.
    /// </summary>
    /// <param name="entityName">
    /// The name of the entity type that wasn't found, e.g. <c>Product</c>. The query helpers use the entity the query
    /// starts from, or <c>item</c> when the query has no entity root.
    /// </param>
    /// <param name="key">The key that was looked up, or <see langword="null"/> when the entity was searched by a query.</param>
    public EntityNotFoundException(string entityName, object? key = null)
        : base(key is null ? $"No {entityName} matched the query." : $"{entityName} {FormatKey(key)} was not found.")
    {
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        EntityName = entityName;
        Key = key;
    }

    /// <summary>The name of the entity type that wasn't found.</summary>
    public string EntityName { get; }

    /// <summary>The key that was looked up (an array for composite keys), or <see langword="null"/> for a query.</summary>
    public object? Key { get; }

    private static string FormatKey(object key) =>
        key is object?[] parts ? $"({string.Join(", ", parts)})" : key.ToString() ?? "";
}
