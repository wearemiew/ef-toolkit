using System.Text.Json;
using EntityFrameworkToolKit.Pagination;

namespace EntityFrameworkToolKit.Tests;

public class PagedResultTests
{
    [Fact]
    public void Serialize_IncludesItemsAndMetadata()
    {
        var result = new PagedResult<int>(new[] { 1, 2 }, page: 2, pageSize: 2, totalCount: 5);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            """{"items":[1,2],"page":2,"pageSize":2,"totalCount":5,"totalPages":3,"hasPreviousPage":true,"hasNextPage":true}""",
            json);
    }

    [Fact]
    public void Deserialize_RoundTripsMetadata()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = new PagedResult<int>(new[] { 1, 2 }, page: 2, pageSize: 2, totalCount: 5);

        var copy = JsonSerializer.Deserialize<PagedResult<int>>(JsonSerializer.Serialize(original, options), options)!;

        Assert.Equal(original.Items, copy.Items);
        Assert.Equal((2, 2, 5, 3), (copy.Page, copy.PageSize, copy.TotalCount, copy.TotalPages));
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(1, -1, 5)]
    [InlineData(1, 10, -1)]
    public void Constructor_InvalidMetadata_Throws(int page, int pageSize, int totalCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(Array.Empty<int>(), page, pageSize, totalCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Constructor_MoreItemsThanPageSize_Throws(int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(new[] { 1, 2, 3 }, page: 1, pageSize, totalCount: 3));
    }

    [Fact]
    public void Map_ProjectsItemsAndKeepsMetadata()
    {
        var result = new PagedResult<int>(new[] { 1, 2 }, page: 2, pageSize: 2, totalCount: 5);

        var mapped = result.Map(x => $"#{x}");

        Assert.Equal(new[] { "#1", "#2" }, mapped.Items);
        Assert.Equal((2, 2, 5), (mapped.Page, mapped.PageSize, mapped.TotalCount));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(5, 0, 0)]
    public void TotalPages_IsCeilingOfTotalOverPageSize(int totalCount, int pageSize, int expected)
    {
        var result = new PagedResult<int>(Array.Empty<int>(), page: 1, pageSize, totalCount);

        Assert.Equal(expected, result.TotalPages);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, false)]
    public void HasPreviousAndNextPage_ReflectPosition(int page, bool hasPrevious, bool hasNext)
    {
        var result = new PagedResult<int>(Array.Empty<int>(), page, pageSize: 10, totalCount: 30);

        Assert.Equal(hasPrevious, result.HasPreviousPage);
        Assert.Equal(hasNext, result.HasNextPage);
    }

    [Fact]
    public void UnknownTotal_SerializesNullTotals()
    {
        var result = new PagedResult<int>(new[] { 1, 2 }, page: 2, pageSize: 2, hasNextPage: true);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            """{"items":[1,2],"page":2,"pageSize":2,"totalCount":null,"totalPages":null,"hasPreviousPage":true,"hasNextPage":true}""",
            json);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownTotal_Deserialize_RoundTripsHasNextPage(bool hasNextPage)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = new PagedResult<int>(new[] { 1, 2 }, page: 2, pageSize: 2, hasNextPage);

        var copy = JsonSerializer.Deserialize<PagedResult<int>>(JsonSerializer.Serialize(original, options), options)!;

        Assert.Equal(original.Items, copy.Items);
        Assert.Null(copy.TotalCount);
        Assert.Equal(hasNextPage, copy.HasNextPage);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void UnknownTotal_Constructor_InvalidMetadata_Throws(int page, int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PagedResult<int>(Array.Empty<int>(), page, pageSize, hasNextPage: false));
    }

    [Fact]
    public void UnknownTotal_Map_KeepsHasNextPage()
    {
        var result = new PagedResult<int>(new[] { 1, 2 }, page: 3, pageSize: 2, hasNextPage: true);

        var mapped = result.Map(x => x * 10);

        Assert.Equal(new[] { 10, 20 }, mapped.Items);
        Assert.Equal((3, 2, (int?)null, true), (mapped.Page, mapped.PageSize, mapped.TotalCount, mapped.HasNextPage));
    }
}
