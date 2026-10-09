namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// Server-side limits applied to a <see cref="PageRequest"/>. Never bind this from client input.
/// </summary>
public sealed record PagingOptions
{
    /// <summary>The options used when none are given: 20 items per page, at most 100.</summary>
    public static PagingOptions Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PagingOptions"/> class.
    /// </summary>
    /// <param name="defaultPageSize">The page size used when the request does not specify one.</param>
    /// <param name="maxPageSize">The largest page size a request may get; larger requested sizes are clamped to it.</param>
    /// <param name="includeTotalCount">
    /// Whether to run a <c>COUNT</c> query for <see cref="PagedResult{T}.TotalCount"/>. Turn it off for endpoints that
    /// don't show a total (e.g. infinite scroll): each page is then a single query.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either size is less than 1, or <paramref name="defaultPageSize"/> exceeds <paramref name="maxPageSize"/>.
    /// </exception>
    public PagingOptions(int defaultPageSize = 20, int maxPageSize = 100, bool includeTotalCount = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(defaultPageSize, maxPageSize);
        DefaultPageSize = defaultPageSize;
        MaxPageSize = maxPageSize;
        IncludeTotalCount = includeTotalCount;
    }

    /// <summary>The page size used when the request does not specify one.</summary>
    public int DefaultPageSize { get; }

    /// <summary>The largest page size a request may get; larger requested sizes are clamped to it.</summary>
    public int MaxPageSize { get; }

    /// <summary>
    /// Whether pages include <see cref="PagedResult{T}.TotalCount"/>. When off, each page is one query that fetches one
    /// extra row to tell whether a next page exists, and the total is reported only on the last page.
    /// </summary>
    public bool IncludeTotalCount { get; }
}
