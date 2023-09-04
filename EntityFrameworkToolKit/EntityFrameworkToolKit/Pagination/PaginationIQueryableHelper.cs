using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Pagination;

public static class PaginationIQueryableHelper
{
    public static async Task<PaginatedIEnumerable<T>> AddPagination<T>(this IQueryable<T> query, int? page, int? size)
    {
        if (!IsPaginationValid(page, size)) return new PaginatedIEnumerable<T>(Enumerable.Empty<T>(), 0);
        return await Build<T>(query, page, size);
    }
    
    public static async Task<PaginatedIEnumerable<T>> AddOptionalPagination<T>(this IQueryable<T> query, int? page, int? size)
    {
        if (IsPaginationValid(page, size)) return await Build<T>(query, page, size);
        var list = await query
            .ToListAsync();
        return new PaginatedIEnumerable<T>(list, list.Count);
    }

    private static async Task<PaginatedIEnumerable<T>> Build<T>(IQueryable<T> query, int? page, int? size)
    {
        var list = query
            .Skip((int)((page!-1) * size!))
            .Take((int)size!)
            .ToListAsync();

        var count = query.CountAsync();
        await Task.WhenAll(list, count);
        return new PaginatedIEnumerable<T>(list.Result, count.Result);
    }

    private static bool IsPaginationValid(int? page, int? size)
    {
        return size is not null && page is not null && size > 0 && page > 0;
    }
}