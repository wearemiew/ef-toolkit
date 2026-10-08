using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// Extension methods that execute an <see cref="IQueryable{T}"/> one page at a time.
/// </summary>
public static class PagedQueryableExtensions
{
    /// <summary>
    /// Executes the query and returns the requested page along with the total item count.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">An ordered query (it must contain OrderBy/OrderByDescending).</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="cancellationToken">A token to cancel the database calls.</param>
    /// <returns>The requested page and its metadata.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> or <paramref name="pageSize"/> is less than 1, or the page lies beyond <see cref="int.MaxValue"/> items.
    /// </exception>
    /// <exception cref="InvalidOperationException">The query is not ordered.</exception>
    public static Task<PagedResult<T>> ToPagedListAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var skip = GetSkip(page, pageSize);
        PaginationGuard.EnsureOrdered(query);

        return FetchPageAsync(query, page, pageSize, skip, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns the requested page when both <paramref name="page"/> and
    /// <paramref name="pageSize"/> are 1 or greater; otherwise returns every item as a single page
    /// whose <see cref="PagedResult{T}.PageSize"/> equals the item count.
    /// </summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="query">The query. It must be ordered when paging is applied.</param>
    /// <param name="page">The 1-based page number, or <see langword="null"/> to return everything.</param>
    /// <param name="pageSize">The number of items per page, or <see langword="null"/> to return everything.</param>
    /// <param name="cancellationToken">A token to cancel the database calls.</param>
    /// <returns>The requested page, or every item, with its metadata.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page lies beyond <see cref="int.MaxValue"/> items.</exception>
    /// <exception cref="InvalidOperationException">Paging is applied and the query is not ordered.</exception>
    public static Task<PagedResult<T>> ToOptionalPagedListAsync<T>(
        this IQueryable<T> query,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return page >= 1 && pageSize >= 1
            ? query.ToPagedListAsync(page.Value, pageSize.Value, cancellationToken)
            : FetchAllAsync(query, cancellationToken);
    }

    private static async Task<PagedResult<T>> FetchPageAsync<T>(
        IQueryable<T> query,
        int page,
        int pageSize,
        int skip,
        CancellationToken cancellationToken)
    {
        var items = await query.Skip(skip).Take(pageSize).ToListAsync(cancellationToken).ConfigureAwait(false);

        // A short page is the last page, so the total is already known — unless it is empty past the
        // first page, where the data may end anywhere before it.
        var isKnownLastPage = items.Count < pageSize && (items.Count > 0 || skip == 0);
        var totalCount = isKnownLastPage
            ? skip + items.Count
            : await query.CountAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<T>(items, page, pageSize, totalCount);
    }

    private static async Task<PagedResult<T>> FetchAllAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        var items = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<T>(items, page: 1, pageSize: items.Count, totalCount: items.Count);
    }

    private static int GetSkip(int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page), page, $"Page {page} with size {pageSize} skips more than {int.MaxValue} items.");
        return (int)skip;
    }
}
