using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkToolKit.Pagination;

/// <summary>
/// A cursor page request as sent by a client, designed to bind from a query string
/// (<c>?pageSize=20&amp;after=eyJz…</c>), e.g. with <c>[AsParameters]</c> or <c>[FromQuery]</c>.
/// </summary>
/// <remarks>
/// Send no cursor for the first page, <see cref="After"/> = a result's <see cref="CursorPagedResult{T}.NextCursor"/>
/// for the next page, or <see cref="Before"/> = its <see cref="CursorPagedResult{T}.PreviousCursor"/> for the previous one.
/// Missing values fall back to <see cref="PagingOptions"/> when the request is executed.
/// </remarks>
public sealed record CursorRequest
{
    internal const string AfterParameter = "after";
    internal const string BeforeParameter = "before";

    /// <summary>Returns the items after this cursor.</summary>
    public string? After { get; init; }

    /// <summary>Returns the items before this cursor.</summary>
    public string? Before { get; init; }

    /// <summary>The requested page size; defaults to <see cref="PagingOptions.DefaultPageSize"/> and is clamped to <see cref="PagingOptions.MaxPageSize"/>.</summary>
    [Range(1, int.MaxValue)]
    public int? PageSize { get; init; }

    /// <summary>
    /// A comma-separated sort specification such as <c>-price,name</c>, for use with
    /// <see cref="Sorting.SortQueryableExtensions.ApplySort{T}"/>. Cursors only work with the sort they were created for.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>
    /// Applies the page-size default and cap, and picks the cursor and its direction.
    /// </summary>
    /// <returns>The page size, the cursor (if any), whether it reads backwards, and the query parameter the cursor came from.</returns>
    /// <exception cref="InvalidQueryRequestException"><see cref="PageSize"/> is less than 1, or both cursors are given.</exception>
    internal (int PageSize, string? Cursor, bool Backward, string CursorParameter) Normalize(PagingOptions options)
    {
        var pageSize = Math.Min(PageSize ?? options.DefaultPageSize, options.MaxPageSize);
        if (pageSize < 1)
            throw new InvalidQueryRequestException($"pageSize must be 1 or greater, but was {pageSize}.", "pageSize");

        var after = string.IsNullOrEmpty(After) ? null : After;
        var before = string.IsNullOrEmpty(Before) ? null : Before;
        if (after is not null && before is not null)
            throw new InvalidQueryRequestException("Use either after or before, not both.", BeforeParameter);

        return before is null
            ? (pageSize, after, false, AfterParameter)
            : (pageSize, before, true, BeforeParameter);
    }
}
