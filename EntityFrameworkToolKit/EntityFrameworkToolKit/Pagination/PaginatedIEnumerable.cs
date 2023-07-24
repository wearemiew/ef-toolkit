using System.Collections;

namespace EntityFrameworkToolKit.Pagination;

public class PaginatedIEnumerable<T>: IEnumerable<T>
{
    public IEnumerable<T> Items { get; set; }
    public int Total { get; set; }

    public PaginatedIEnumerable(IEnumerable<T> list, int total)
    {
        Items = list;
        Total = total;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}