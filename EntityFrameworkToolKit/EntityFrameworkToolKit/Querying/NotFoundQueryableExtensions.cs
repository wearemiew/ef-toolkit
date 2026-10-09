using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkToolKit.Querying;

/// <summary>
/// Loads an entity or throws <see cref="EntityNotFoundException"/>, so "get by id" endpoints don't repeat
/// <c>if (entity is null) return NotFound()</c>. Map the exception to a 404 once.
/// </summary>
public static class NotFoundQueryableExtensions
{
    /// <summary>Returns the first item of the query, or throws when it is empty.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The first item.</returns>
    /// <exception cref="EntityNotFoundException">The query returned no items.</exception>
    public static Task<T> FirstOrNotFoundAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FirstAsync(query, take: 1, cancellationToken);
    }

    /// <summary>Returns the first item matching <paramref name="predicate"/>, or throws when none does.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="predicate">The condition the item must match.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The first matching item.</returns>
    /// <exception cref="EntityNotFoundException">No item matched.</exception>
    public static Task<T> FirstOrNotFoundAsync<T>(
        this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(predicate);
        return query.Where(predicate).FirstOrNotFoundAsync(cancellationToken);
    }

    /// <summary>Returns the only item of the query, or throws when it is empty.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The only item.</returns>
    /// <exception cref="EntityNotFoundException">The query returned no items.</exception>
    /// <exception cref="InvalidOperationException">The query returned more than one item.</exception>
    public static Task<T> SingleOrNotFoundAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FirstAsync(query, take: 2, cancellationToken);
    }

    /// <summary>Returns the only item matching <paramref name="predicate"/>, or throws when none does.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="predicate">The condition the item must match.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The only matching item.</returns>
    /// <exception cref="EntityNotFoundException">No item matched.</exception>
    /// <exception cref="InvalidOperationException">More than one item matched.</exception>
    public static Task<T> SingleOrNotFoundAsync<T>(
        this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(predicate);
        return query.Where(predicate).SingleOrNotFoundAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the entity with the given primary key (returning an already tracked instance without a query), or throws.
    /// </summary>
    /// <remarks>
    /// For a composite key, pass the values as an <c>object?[]</c> (the other overload); an array typed as
    /// <see cref="object"/> binds here and is looked up as a single key value.
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="set">The entity set.</param>
    /// <param name="key">The primary key value.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The entity.</returns>
    /// <exception cref="EntityNotFoundException">No entity has that key.</exception>
    public static Task<T> FindOrNotFoundAsync<T>(this DbSet<T> set, object key, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        return set.FindOrNotFoundAsync(new[] { key }, cancellationToken);
    }

    /// <summary>
    /// Finds the entity with the given (composite) primary key (returning an already tracked instance without a query), or throws.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="set">The entity set.</param>
    /// <param name="keyValues">The primary key values, in key order.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The entity.</returns>
    /// <exception cref="EntityNotFoundException">No entity has that key.</exception>
    public static Task<T> FindOrNotFoundAsync<T>(this DbSet<T> set, object?[] keyValues, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(keyValues);
        var key = keyValues.Length == 1 ? keyValues[0] : keyValues;
        return FindAsync(set, keyValues, key, cancellationToken);
    }

    // The helpers below run after argument validation, so bad arguments throw synchronously, not from the task.

    // Fetches up to `take` rows instead of FirstOrDefault/SingleOrDefault, which can't tell an empty result from a
    // default element (0 for a projected int, null for a projected nullable column).
    private static async Task<T> FirstAsync<T>(IQueryable<T> query, int take, CancellationToken cancellationToken)
    {
        var items = await query.Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Count switch
        {
            0 => throw new EntityNotFoundException(EntityName(query.Expression)),
            1 => items[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element."),
        };
    }

    private static async Task<T> FindAsync<T>(DbSet<T> set, object?[] keyValues, object? key, CancellationToken cancellationToken)
        where T : class =>
        await set.FindAsync(keyValues, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException(set.EntityType.ShortName(), key);

    // The entity the query starts from, even after a Select: a projected type's name (Int32, a DTO, an anonymous
    // type) would be meaningless in a 404 and could leak internal names.
    private static string EntityName(Expression expression)
    {
        while (expression is MethodCallExpression { Arguments.Count: > 0 } call)
            expression = call.Arguments[0];
        return expression is EntityQueryRootExpression root ? root.EntityType.ShortName() : "item";
    }
}
