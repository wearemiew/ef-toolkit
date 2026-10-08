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
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either size is less than 1, or <paramref name="defaultPageSize"/> exceeds <paramref name="maxPageSize"/>.
    /// </exception>
    public PagingOptions(int defaultPageSize = 20, int maxPageSize = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(defaultPageSize, maxPageSize);
        DefaultPageSize = defaultPageSize;
        MaxPageSize = maxPageSize;
    }

    /// <summary>The page size used when the request does not specify one.</summary>
    public int DefaultPageSize { get; }

    /// <summary>The largest page size a request may get; larger requested sizes are clamped to it.</summary>
    public int MaxPageSize { get; }
}
