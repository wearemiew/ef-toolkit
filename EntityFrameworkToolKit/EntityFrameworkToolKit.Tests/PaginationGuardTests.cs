using EntityFrameworkToolKit.Pagination;

namespace EntityFrameworkToolKit.Tests;

public class PaginationGuardTests
{
    private static readonly IQueryable<int> Source = new[] { 3, 1, 2 }.AsQueryable();

    public static TheoryData<IQueryable> OrderedQueries => new()
    {
        Source.OrderBy(x => x),
        Source.OrderByDescending(x => x),
        Source.OrderBy(x => x).ThenBy(x => -x),
        Source.OrderBy(x => x).ThenByDescending(x => -x),
        Source.Order(),
        Source.OrderDescending(),
        Source.OrderBy(x => x).Where(x => x > 1),
        Source.OrderBy(x => x).Select(x => x.ToString()),
    };

    [Theory]
    [MemberData(nameof(OrderedQueries))]
    public void IsOrdered_OrderedQuery_ReturnsTrue(IQueryable query)
    {
        Assert.True(PaginationGuard.IsOrdered(query));
    }

    [Fact]
    public void IsOrdered_UnorderedQuery_ReturnsFalse()
    {
        Assert.False(PaginationGuard.IsOrdered(Source.Where(x => x > 1)));
    }

    [Fact]
    public void EnsureOrdered_UnorderedQuery_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PaginationGuard.EnsureOrdered(Source));

        Assert.Contains("OrderBy", ex.Message);
    }
}
