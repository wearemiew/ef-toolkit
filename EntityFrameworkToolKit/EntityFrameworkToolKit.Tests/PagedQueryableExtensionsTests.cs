using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Tests.TestHelpers;

namespace EntityFrameworkToolKit.Tests;

public sealed class PagedQueryableExtensionsTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContext.CreateSeeded(50);

    public void Dispose() => _db.Dispose();

    private IQueryable<MyEntity> Ordered => _db.Entities.OrderBy(e => e.Id);

    [Fact]
    public async Task ToPagedListAsync_ReturnsCorrectPage()
    {
        var result = await Ordered.ToPagedListAsync(page: 2, pageSize: 10);

        Assert.Equal(Enumerable.Range(11, 10), result.Items.Select(e => e.Id));
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(50, result.TotalCount);
        Assert.Equal(5, result.TotalPages);
    }

    [Fact]
    public async Task ToPagedListAsync_LastPartialPage_TotalIsCorrect()
    {
        var result = await Ordered.ToPagedListAsync(page: 3, pageSize: 20);

        Assert.Equal(Enumerable.Range(41, 10), result.Items.Select(e => e.Id));
        Assert.Equal(50, result.TotalCount);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task ToPagedListAsync_LastPageExactlyFull_CountsAndHasNoNextPage()
    {
        var result = await Ordered.ToPagedListAsync(page: 5, pageSize: 10);

        Assert.Equal(Enumerable.Range(41, 10), result.Items.Select(e => e.Id));
        Assert.Equal(50, result.TotalCount);
        Assert.False(result.HasNextPage);
        Assert.Equal(2, _db.ExecutedCommands.Count);
    }

    [Fact]
    public async Task ToPagedListAsync_ShortPage_SkipsCountQuery()
    {
        var result = await Ordered.ToPagedListAsync(page: 3, pageSize: 20);

        Assert.Equal(50, result.TotalCount);
        Assert.Single(_db.ExecutedCommands);
    }

    [Fact]
    public async Task ToPagedListAsync_PageBeyondEnd_ReturnsEmptyWithTotal()
    {
        var result = await Ordered.ToPagedListAsync(page: 10, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(50, result.TotalCount);
    }

    [Fact]
    public async Task ToPagedListAsync_EmptyTable_ReturnsEmptyWithZeroTotal()
    {
        using var empty = TestDbContext.CreateSeeded(0);

        var result = await empty.Entities.OrderBy(e => e.Id).ToPagedListAsync(page: 1, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-4, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -6)]
    public async Task ToPagedListAsync_InvalidArguments_Throws(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Ordered.ToPagedListAsync(page, pageSize));
    }

    [Fact]
    public async Task ToPagedListAsync_SkipOverflow_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Ordered.ToPagedListAsync(int.MaxValue, 100));
    }

    [Fact]
    public async Task ToPagedListAsync_UnorderedQuery_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.Entities.ToPagedListAsync(1, 10));
    }

    [Fact]
    public async Task ToPagedListAsync_OrderedOnlyInsideSubquery_Throws()
    {
        // The only OrderBy is a Queryable call inside the projection's subquery, not on the paged query itself.
        var query = _db.Entities.Select(e => new
        {
            e.Id,
            FirstId = _db.Entities.OrderBy(x => x.Id).Select(x => x.Id).FirstOrDefault(),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToPagedListAsync(1, 10));
    }

    [Fact]
    public async Task ToPagedListAsync_OrderedThenProjected_Works()
    {
        var result = await _db.Entities
            .OrderByDescending(e => e.Id)
            .Select(e => new { e.Id })
            .ToPagedListAsync(page: 1, pageSize: 3);

        Assert.Equal(new[] { 50, 49, 48 }, result.Items.Select(e => e.Id));
        Assert.Equal(50, result.TotalCount);
    }

    [Fact]
    public async Task ToPagedListAsync_OrderedThenFiltered_Works()
    {
        var result = await Ordered
            .Where(e => e.Id % 2 == 0)
            .ToPagedListAsync(page: 2, pageSize: 5);

        Assert.Equal(new[] { 12, 14, 16, 18, 20 }, result.Items.Select(e => e.Id));
        Assert.Equal(25, result.TotalCount);
    }

    [Fact]
    public async Task ToPagedListAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ordered.ToPagedListAsync(1, 10, cts.Token));
    }

    [Theory]
    [InlineData(1, 2, 2)]
    [InlineData(2, 10, 10)]
    [InlineData(-4, -6, 50)]
    [InlineData(0, 10, 50)]
    [InlineData(null, 10, 50)]
    [InlineData(null, null, 50)]
    [InlineData(1, null, 50)]
    public async Task ToOptionalPagedListAsync_ReturnsExpectedCount(int? page, int? pageSize, int expected)
    {
        var result = await Ordered.ToOptionalPagedListAsync(page, pageSize);

        Assert.Equal(expected, result.Items.Count);
        Assert.Equal(50, result.TotalCount);
    }

    [Fact]
    public async Task ToOptionalPagedListAsync_WithoutPaging_ReturnsSinglePage()
    {
        var result = await _db.Entities.ToOptionalPagedListAsync(null, null);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task ToOptionalPagedListAsync_WithoutPagingOnEmptyTable_ReturnsEmptyPage()
    {
        using var empty = TestDbContext.CreateSeeded(0);

        var result = await empty.Entities.ToOptionalPagedListAsync(null, null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.PageSize);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void BothMethods_NullQuery_ThrowSynchronously()
    {
        IQueryable<MyEntity> query = null!;

        // Discarding the task: the exception must be thrown by the call itself, not stored in the task.
        Assert.Throws<ArgumentNullException>(() => { _ = query.ToPagedListAsync(1, 10); });
        Assert.Throws<ArgumentNullException>(() => { _ = query.ToOptionalPagedListAsync(1, 10); });
    }

    [Fact]
    public async Task ToOptionalPagedListAsync_ValidPaging_RequiresOrdering()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.Entities.ToOptionalPagedListAsync(1, 10));
    }

    [Fact]
    public async Task ToOptionalPagedListAsync_SkipOverflow_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Ordered.ToOptionalPagedListAsync(int.MaxValue, 100));
    }

    [Fact]
    public async Task ToPagedListAsync_WithoutTotal_RunsOneQuery_AndSetsHasNextPage()
    {
        var result = await Ordered.ToPagedListAsync(page: 2, pageSize: 10, includeTotalCount: false);

        Assert.Equal(Enumerable.Range(11, 10), result.Items.Select(e => e.Id));
        Assert.Null(result.TotalCount);
        Assert.Null(result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
        var command = Assert.Single(_db.ExecutedCommands);
        Assert.DoesNotContain("COUNT", command, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(3, 20, 10)]
    [InlineData(5, 10, 10)]
    public async Task ToPagedListAsync_WithoutTotal_LastPage_ReportsTotal(int page, int pageSize, int expectedItems)
    {
        var result = await Ordered.ToPagedListAsync(page, pageSize, includeTotalCount: false);

        Assert.Equal(expectedItems, result.Items.Count);
        Assert.Equal(50, result.TotalCount);
        Assert.False(result.HasNextPage);
        Assert.Single(_db.ExecutedCommands);
    }

    [Fact]
    public async Task ToPagedListAsync_WithoutTotal_PageBeyondEnd_IsEmptyWithUnknownTotal()
    {
        var result = await Ordered.ToPagedListAsync(page: 10, pageSize: 10, includeTotalCount: false);

        Assert.Empty(result.Items);
        Assert.Null(result.TotalCount);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task ToPagedListAsync_WithoutTotal_EmptyTable_ReportsZeroTotal()
    {
        using var empty = TestDbContext.CreateSeeded(0);

        var result = await empty.Entities.OrderBy(e => e.Id).ToPagedListAsync(page: 1, pageSize: 10, includeTotalCount: false);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task ToPagedListAsync_WithoutTotal_StillValidatesAndRequiresOrdering()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Ordered.ToPagedListAsync(0, 10, includeTotalCount: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.Entities.ToPagedListAsync(1, 10, includeTotalCount: false));
    }

    [Fact]
    public async Task ToPagedListAsync_WithoutTotal_MaxPageSize_DoesNotOverflow()
    {
        var result = await Ordered.ToPagedListAsync(page: 1, pageSize: int.MaxValue, includeTotalCount: false);

        Assert.Equal(50, result.Items.Count);
        Assert.Equal(50, result.TotalCount);
        Assert.False(result.HasNextPage);
    }
}
