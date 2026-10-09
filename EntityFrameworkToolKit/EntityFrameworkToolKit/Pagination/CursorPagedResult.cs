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
    /// <param name="nextCursor">The cursor of the next page, or <see langword="null"/> when this is the last page.</param>
    /// <param name="previousCursor">The cursor of the previous page, or <see langword="null"/> when this is the first page.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pageSize"/> is less than 1, or <paramref name="items"/> holds more than <paramref name="pageSize"/> items.
    /// </exception>
    public CursorPagedResult(IReadOnlyList<T> items, int pageSize, string? nextCursor, string? previousCursor)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        if (items.Count > pageSize)
            throw new ArgumentOutOfRangeException(nameof(items), items.Count, $"A page of size {pageSize} cannot hold {items.Count} items.");
        Items = items;
        PageSize = pageSize;
        NextCursor = nextCursor;
        PreviousCursor = previousCursor;
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The maximum number of items per page.</summary>
    public int PageSize { get; }

    /// <summary>Pass as <see cref="CursorRequest.After"/> to get the next page; <see langword="null"/> on the last page.</summary>
    public string? NextCursor { get; }

    /// <summary>Pass as <see cref="CursorRequest.Before"/> to get the previous page; <see langword="null"/> on the first page.</summary>
    public string? PreviousCursor { get; }

    /// <summary>Whether a page exists after this one.</summary>
    public bool HasNextPage => NextCursor is not null;

    /// <summary>Whether a page exists before this one.</summary>
    public bool HasPreviousPage => PreviousCursor is not null;

    /// <summary>
    /// Projects each item into a new form, keeping the page size and cursors (e.g. entities to DTOs).
    /// </summary>
    /// <typeparam name="TResult">The type of the projected items.</typeparam>
    /// <param name="selector">The projection applied to each item.</param>
    /// <returns>A result with the projected items and the same <see cref="PageSize"/> and cursors.</returns>
    public CursorPagedResult<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new CursorPagedResult<TResult>(Items.Select(selector).ToList(), PageSize, NextCursor, PreviousCursor);
    }
}
