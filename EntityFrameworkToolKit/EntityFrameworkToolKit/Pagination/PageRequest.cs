using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// A page request as sent by a client, designed to bind from a query string
/// (<c>?page=2&amp;pageSize=50&amp;sort=-price,name</c>), e.g. with <c>[AsParameters]</c> or <c>[FromQuery]</c>.
/// </summary>
/// <remarks>
/// Values are nullable so that every query parameter is optional. Missing values fall back to
/// <see cref="PagingOptions"/> when the request is executed. The <see cref="RangeAttribute"/>s document the valid
/// range (e.g. in OpenAPI) and are enforced by MVC model validation; either way, executing the request rejects
/// out-of-range values with <see cref="InvalidQueryRequestException"/>.
/// </remarks>
public sealed record PageRequest
{
    /// <summary>The 1-based page number; defaults to 1.</summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    /// <summary>The requested page size; defaults to <see cref="PagingOptions.DefaultPageSize"/> and is clamped to <see cref="PagingOptions.MaxPageSize"/>.</summary>
    [Range(1, int.MaxValue)]
    public int? PageSize { get; init; }

    /// <summary>
    /// A comma-separated sort specification such as <c>-price,name</c>, for use with
    /// <see cref="Sorting.SortQueryableExtensions.ApplySort{T}"/>.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>
    /// Applies defaults and the page-size cap, and rejects values no page can satisfy.
    /// </summary>
    /// <exception cref="InvalidQueryRequestException">
    /// <see cref="Page"/> or <see cref="PageSize"/> is less than 1, or the page lies beyond <see cref="int.MaxValue"/> items.
    /// </exception>
    internal (int Page, int PageSize) Normalize(PagingOptions options)
    {
        var page = Page ?? 1;
        var pageSize = Math.Min(PageSize ?? options.DefaultPageSize, options.MaxPageSize);

        if (page < 1)
            throw new InvalidQueryRequestException($"page must be 1 or greater, but was {page}.", "page");
        if (pageSize < 1)
            throw new InvalidQueryRequestException($"pageSize must be 1 or greater, but was {pageSize}.", "pageSize");
        if ((long)(page - 1) * pageSize > int.MaxValue)
            throw new InvalidQueryRequestException($"page {page} is too large for pageSize {pageSize}.", "page");

        return (page, pageSize);
    }
}
