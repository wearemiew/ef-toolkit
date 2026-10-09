namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// One page of a cursor-paged query, with the cursors that fetch the neighbouring pages.
/// </summary>
/// <remarks>
/// There is no total count: counting would scan the whole result, which is what cursor paging avoids.
/// Like <see cref="PagedResult{T}"/>, this type is deliberately not enumerable, so it serializes as an object.
/// </remarks>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed class CursorPagedResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CursorPagedResult{T}"/> class.
    /// </summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="startCursor">The cursor of the first item, or <see langword="null"/> when the page is empty.</param>
    /// <param name="endCursor">The cursor of the last item, or <see langword="null"/> when the page is empty.</param>
    /// <param name="hasPreviousPage">Whether a page exists before this one.</param>
    /// <param name="hasNextPage">Whether a page exists after this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pageSize"/> is less than 1, or <paramref name="items"/> holds more than <paramref name="pageSize"/> items.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A neighbouring page is flagged without the cursor that reaches it (<paramref name="startCursor"/> for the
    /// previous page, <paramref name="endCursor"/> for the next).
    /// </exception>
    public CursorPagedResult(
        IReadOnlyList<T> items, int pageSize, string? startCursor, string? endCursor, bool hasPreviousPage, bool hasNextPage)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        if (items.Count > pageSize)
            throw new ArgumentOutOfRangeException(nameof(items), items.Count, $"A page of size {pageSize} cannot hold {items.Count} items.");
        if (hasPreviousPage && startCursor is null)
            throw new ArgumentException("A previous page needs a start cursor to reach it.", nameof(startCursor));
        if (hasNextPage && endCursor is null)
            throw new ArgumentException("A next page needs an end cursor to reach it.", nameof(endCursor));
        Items = items;
        PageSize = pageSize;
        StartCursor = startCursor;
        EndCursor = endCursor;
        HasPreviousPage = hasPreviousPage;
        HasNextPage = hasNextPage;
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The maximum number of items per page.</summary>
    public int PageSize { get; }

    /// <summary>Pass as <see cref="CursorRequest.After"/> to get the next page; <see langword="null"/> on the last page.</summary>
    public string? NextCursor => HasNextPage ? EndCursor : null;

    /// <summary>Pass as <see cref="CursorRequest.Before"/> to get the previous page; <see langword="null"/> on the first page.</summary>
    public string? PreviousCursor => HasPreviousPage ? StartCursor : null;

    /// <summary>
    /// The cursor of the first item, even on the first page; <see langword="null"/> only when the page is empty.
    /// Pass it as <see cref="CursorRequest.Before"/> later to check for items inserted before it.
    /// </summary>
    public string? StartCursor { get; }

    /// <summary>
    /// The cursor of the last item, even on the last page; <see langword="null"/> only when the page is empty.
    /// Keep it to resume or poll later (sync jobs, "load newer"): <c>after = page.EndCursor ?? after</c>.
    /// </summary>
    public string? EndCursor { get; }

    /// <summary>Whether a page exists after this one.</summary>
    public bool HasNextPage { get; }

    /// <summary>Whether a page exists before this one.</summary>
    public bool HasPreviousPage { get; }

    /// <summary>
    /// Projects each item into a new form, keeping the page size and cursors (e.g. entities to DTOs).
    /// </summary>
    /// <typeparam name="TResult">The type of the projected items.</typeparam>
    /// <param name="selector">The projection applied to each item.</param>
    /// <returns>A result with the projected items and the same <see cref="PageSize"/>, cursors and flags.</returns>
    public CursorPagedResult<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new CursorPagedResult<TResult>(
            Items.Select(selector).ToList(), PageSize, StartCursor, EndCursor, HasPreviousPage, HasNextPage);
    }
}
