namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// One page of query results together with the metadata needed to render pagination controls.
/// </summary>
/// <remarks>
/// This type deliberately does not implement <see cref="IEnumerable{T}"/>, so serializers emit it as an object
/// (items plus metadata) rather than as a bare array.
/// </remarks>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagedResult{T}"/> class.
    /// </summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The maximum number of items per page; 0 only for an empty, unpaged result.</param>
    /// <param name="totalCount">The total number of items across all pages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> is less than 1, <paramref name="pageSize"/> or <paramref name="totalCount"/> is negative,
    /// or <paramref name="items"/> holds more than <paramref name="pageSize"/> items.
    /// </exception>
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        // TotalCount is not checked against Items: it comes from a separate COUNT query, which can see fewer rows
        // than the page query if rows are deleted in between.
        if (items.Count > pageSize)
            throw new ArgumentOutOfRangeException(nameof(items), items.Count, $"A page of size {pageSize} cannot hold {items.Count} items.");
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The 1-based page number.</summary>
    public int Page { get; }

    /// <summary>The maximum number of items per page.</summary>
    public int PageSize { get; }

    /// <summary>The total number of items across all pages.</summary>
    public int TotalCount { get; }

    /// <summary>The number of pages needed to hold <see cref="TotalCount"/> items.</summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    /// <summary>Whether a page exists before this one.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>Whether a page exists after this one.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Projects each item into a new form, keeping the paging metadata (e.g. entities to DTOs).
    /// </summary>
    /// <typeparam name="TResult">The type of the projected items.</typeparam>
    /// <param name="selector">The projection applied to each item.</param>
    /// <returns>A result with the projected items and the same <see cref="Page"/>, <see cref="PageSize"/> and <see cref="TotalCount"/>.</returns>
    public PagedResult<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new PagedResult<TResult>(Items.Select(selector).ToList(), Page, PageSize, TotalCount);
    }
}
