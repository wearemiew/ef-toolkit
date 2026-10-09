using EntityFrameworkToolKit.Pagination.Cursors;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// Extension methods that page an <see cref="IQueryable{T}"/> by cursor (keyset pagination): each page is "the next
/// N rows after this row", which stays fast at any depth and doesn't skip or repeat rows when data changes.
/// </summary>
public static class CursorQueryableExtensions
{
    /// <summary>
    /// Executes the query for a client's <see cref="CursorRequest"/>, applying <see cref="PagingOptions.Default"/>
    /// (20 items per page when unspecified, at most 100).
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">
    /// A query whose last step is its ordering (e.g. from <c>ApplySort</c>). The last sort key must be unique, such as the Id.
    /// </param>
    /// <param name="request">The cursor request, typically bound from the query string.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The requested page and the cursors of its neighbours.</returns>
    /// <exception cref="InvalidQueryRequestException">The page size is less than 1, both cursors are given, or a cursor is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The query is unordered, its ordering isn't the last step, or a sort key is nullable or of an unsupported type.
    /// </exception>
    public static Task<CursorPagedResult<T>> ToCursorPagedListAsync<T>(
        this IQueryable<T> query,
        CursorRequest request,
        CancellationToken cancellationToken = default) =>
        query.ToCursorPagedListAsync(request, PagingOptions.Default, cancellationToken);

    /// <summary>
    /// Executes the query for a client's <see cref="CursorRequest"/>, applying the given server-side <paramref name="options"/>.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">
    /// A query whose last step is its ordering (e.g. from <c>ApplySort</c>). The last sort key must be unique, such as the Id.
    /// </param>
    /// <param name="request">The cursor request, typically bound from the query string.</param>
    /// <param name="options">The default and maximum page size.</param>
    /// <param name="cancellationToken">A token to cancel the database call.</param>
    /// <returns>The requested page and the cursors of its neighbours.</returns>
    /// <exception cref="InvalidQueryRequestException">The page size is less than 1, both cursors are given, or a cursor is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The query is unordered, its ordering isn't the last step, or a sort key is nullable or of an unsupported type.
    /// </exception>
    public static Task<CursorPagedResult<T>> ToCursorPagedListAsync<T>(
        this IQueryable<T> query,
        CursorRequest request,
        PagingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var (pageSize, cursor, backward, cursorParameter) = request.Normalize(options);
        var ordering = CursorOrdering<T>.From(query);
        var types = ordering.Keys.Select(k => k.Type).ToList();
        var position = cursor is null ? null : CursorCodec.Decode(cursor, ordering.Fingerprint, types, cursorParameter);

        return FetchAsync(ordering, types, pageSize, position, backward, cancellationToken);
    }

    private static async Task<CursorPagedResult<T>> FetchAsync<T>(
        CursorOrdering<T> ordering,
        IReadOnlyList<Type> types,
        int pageSize,
        object[]? position,
        bool backward,
        CancellationToken cancellationToken)
    {
        // Backward pages are read in reverse order from the cursor, then flipped back. One extra row tells whether
        // another page exists in the reading direction.
        var filter = position is null ? null : KeysetPredicateBuilder.Build<T>(ordering.Keys, position, backward);
        var take = pageSize == int.MaxValue ? pageSize : pageSize + 1;
        var page = ordering.BuildPage(filter, reverse: backward, take);

        var items = await page.ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        if (backward)
            items.Reverse();

        // Coming from a cursor means a row exists on that side. An empty page (the rows around the cursor were
        // deleted, or nothing matches) has no row to point from, so it has no cursors: the client starts over, or
        // keeps the cursor it sent. A non-empty page always has start and end cursors, even at the ends of the data,
        // so a client can resume from there later.
        var hasNext = items.Count > 0 && (backward || hasMore);
        var hasPrevious = items.Count > 0 && (backward ? hasMore : position is not null);
        string Boundary(T item) => CursorCodec.Encode(ordering.Fingerprint, ordering.ReadKeys(item), types);

        return new CursorPagedResult<T>(
            items,
            pageSize,
            startCursor: items.Count > 0 ? Boundary(items[0]) : null,
            endCursor: items.Count > 0 ? Boundary(items[^1]) : null,
            hasPreviousPage: hasPrevious,
            hasNextPage: hasNext);
    }
}
