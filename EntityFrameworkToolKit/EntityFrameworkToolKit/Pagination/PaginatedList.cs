using System.Collections;

namespace Miew.Core;

public class PaginatedList<T> : IList<T>
{
    public List<T> Items { get; set; }
    public int Total { get; set; }

    public PaginatedList(List<T> list, int total)
    {
        Items = list;
        Total = total;
    }
    public IEnumerator<T> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(T item)
    {
        Items.Add(item);
    }

    public void Clear()
    {
        Items.Clear();
    }

    public bool Contains(T item)
    {
        return Items.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        Items.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        return Items.Remove(item);
    }

    public int Count => Items.Count;
    public bool IsReadOnly => false;
    public int IndexOf(T item)
    {
        return Items.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        Items.Insert(index, item);
    }

    public void RemoveAt(int index)
    {
        Items.RemoveAt(index);
    }

    public T this[int index]
    {
        get => Items[index];
        set => Items[index] = value;
    }
}