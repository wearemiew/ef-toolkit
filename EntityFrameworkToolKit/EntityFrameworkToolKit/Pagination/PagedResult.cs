using System.Text.Json.Serialization;

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
        : this(items, page, pageSize)
    {
        // TotalCount is not checked against Items: it comes from a separate COUNT query, which can see fewer rows
        // than the page query if rows are deleted in between.
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        TotalCount = totalCount;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PagedResult{T}"/> class for a page whose total item count was not
    /// computed (see <see cref="PagingOptions.IncludeTotalCount"/>).
    /// </summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="hasNextPage">Whether a page exists after this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> or <paramref name="pageSize"/> is less than 1, or <paramref name="items"/> holds more than
    /// <paramref name="pageSize"/> items.
    /// </exception>
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, bool hasNextPage)
        : this(items, page, pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        _hasNextPage = hasNextPage;
    }

    // For deserialization: JSON carries both, and the total wins when it is known.
    [JsonConstructor]
    private PagedResult(IReadOnlyList<T> items, int page, int pageSize, int? totalCount, bool hasNextPage)
        : this(items, page, pageSize)
    {
        if (totalCount is { } total)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(total, nameof(totalCount));
            TotalCount = total;
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
            _hasNextPage = hasNextPage;
        }
    }

    private PagedResult(IReadOnlyList<T> items, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(pageSize);
        if (items.Count > pageSize)
            throw new ArgumentOutOfRangeException(nameof(items), items.Count, $"A page of size {pageSize} cannot hold {items.Count} items.");
        Items = items;
        Page = page;
        PageSize = pageSize;
    }

    private readonly bool _hasNextPage;

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The 1-based page number.</summary>
    public int Page { get; }

    /// <summary>The maximum number of items per page.</summary>
    public int PageSize { get; }

    /// <summary>
    /// The total number of items across all pages, or <see langword="null"/> when it was not computed
    /// (<see cref="PagingOptions.IncludeTotalCount"/> is off and this is not the last page).
    /// </summary>
    public int? TotalCount { get; }

    /// <summary>The number of pages needed to hold <see cref="TotalCount"/> items, or <see langword="null"/> when the total is unknown.</summary>
    public int? TotalPages => TotalCount is not { } total ? null : PageSize == 0 ? 0 : (int)(((long)total + PageSize - 1) / PageSize);

    /// <summary>Whether a page exists before this one.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>Whether a page exists after this one.</summary>
    public bool HasNextPage => TotalCount is null ? _hasNextPage : Page < TotalPages;

    /// <summary>
    /// Projects each item into a new form, keeping the paging metadata (e.g. entities to DTOs).
    /// </summary>
    /// <typeparam name="TResult">The type of the projected items.</typeparam>
    /// <param name="selector">The projection applied to each item.</param>
    /// <returns>A result with the projected items and the same <see cref="Page"/>, <see cref="PageSize"/>, <see cref="TotalCount"/> and <see cref="HasNextPage"/>.</returns>
    public PagedResult<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var items = Items.Select(selector).ToList();
        return TotalCount is { } total
            ? new PagedResult<TResult>(items, Page, PageSize, total)
            : new PagedResult<TResult>(items, Page, PageSize, HasNextPage);
    }
}
