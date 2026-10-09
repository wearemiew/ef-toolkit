using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Tests.TestHelpers;

namespace EntityFrameworkToolKit.Tests;

public sealed class PageRequestTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContext.CreateSeeded(50);

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(3, null, 3, 20)]
    [InlineData(null, 7, 1, 7)]
    [InlineData(2, 100, 2, 100)]
    [InlineData(2, 500, 2, 100)]
    public void Normalize_AppliesDefaultsAndClampsPageSize(int? page, int? pageSize, int expectedPage, int expectedPageSize)
    {
        var request = new PageRequest { Page = page, PageSize = pageSize };

        Assert.Equal((expectedPage, expectedPageSize), request.Normalize(PagingOptions.Default));
    }

    [Fact]
    public void Normalize_UsesCustomOptions()
    {
        var options = new PagingOptions(defaultPageSize: 5, maxPageSize: 8);

        Assert.Equal((1, 5), new PageRequest().Normalize(options));
        Assert.Equal((1, 8), new PageRequest { PageSize = 9 }.Normalize(options));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 0)]
    [InlineData(10, 5)]
    public void PagingOptions_InvalidLimits_ThrowAtConstruction(int defaultPageSize, int maxPageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagingOptions(defaultPageSize, maxPageSize));
    }

    [Theory]
    [InlineData(0, null, "page")]
    [InlineData(-1, 10, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, -5, "pageSize")]
    [InlineData(int.MaxValue, 100, "page")]
    public void Normalize_InvalidClientInput_ThrowsNamingTheQueryParameter(int? page, int? pageSize, string paramName)
    {
        var request = new PageRequest { Page = page, PageSize = pageSize };

        var ex = Assert.Throws<InvalidQueryRequestException>(() => request.Normalize(PagingOptions.Default));

        Assert.Equal(paramName, ex.ParamName);
        Assert.DoesNotContain("(Parameter", ex.Message);
    }

    [Fact]
    public async Task ToPagedListAsync_PageRequest_UsesDefaults()
    {
        var result = await _db.Entities.OrderBy(e => e.Id).ToPagedListAsync(new PageRequest());

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(Enumerable.Range(1, 20), result.Items.Select(e => e.Id));
        Assert.Equal(50, result.TotalCount);
    }

    [Fact]
    public async Task ToPagedListAsync_PageRequest_ClampsPageSize()
    {
        var options = new PagingOptions(defaultPageSize: 10, maxPageSize: 15);

        var result = await _db.Entities.OrderBy(e => e.Id)
            .ToPagedListAsync(new PageRequest { Page = 2, PageSize = 1000 }, options);

        Assert.Equal(15, result.PageSize);
        Assert.Equal(Enumerable.Range(16, 15), result.Items.Select(e => e.Id));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, null)]
    public async Task ToPagedListAsync_PageRequest_InvalidValues_Throw(int? page, int? pageSize)
    {
        var request = new PageRequest { Page = page, PageSize = pageSize };

        await Assert.ThrowsAsync<InvalidQueryRequestException>(() => _db.Entities.OrderBy(e => e.Id).ToPagedListAsync(request));
    }

    [Fact]
    public async Task ToPagedListAsync_PageRequest_OptionsWithoutTotal_SkipsCount()
    {
        var options = new PagingOptions(defaultPageSize: 10, includeTotalCount: false);

        var result = await _db.Entities.OrderBy(e => e.Id).ToPagedListAsync(new PageRequest(), options);

        Assert.Equal(10, result.Items.Count);
        Assert.Null(result.TotalCount);
        Assert.True(result.HasNextPage);
        var command = Assert.Single(_db.ExecutedCommands);
        Assert.DoesNotContain("COUNT", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PagingOptions_IncludesTotalCountByDefault()
    {
        Assert.True(PagingOptions.Default.IncludeTotalCount);
        Assert.True(new PagingOptions().IncludeTotalCount);
    }
}
