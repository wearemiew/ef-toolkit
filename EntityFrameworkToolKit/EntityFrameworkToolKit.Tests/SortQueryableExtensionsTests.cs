using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Sorting;
using EntityFrameworkToolKit.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Tests;

public sealed class SortQueryableExtensionsTests : IDisposable
{
    private static readonly SortMap<MyEntity> Sorts = new SortMap<MyEntity>()
        .Add("name", e => e.Name)
        .Add("price", e => e.Price)
        .Add("id", e => e.Id)
        .Default(e => e.Id);

    private readonly TestDbContext _db = TestDbContext.CreateSeeded(12);

    public void Dispose() => _db.Dispose();

    private int[] SortedIds(string? sort) => _db.Entities.ApplySort(sort, Sorts).Select(e => e.Id).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ApplySort_EmptySort_UsesDefault(string? sort)
    {
        Assert.Equal(Enumerable.Range(1, 12), SortedIds(sort));
    }

    [Fact]
    public void ApplySort_Descending_ReversesOrder()
    {
        Assert.Equal(Enumerable.Range(1, 12).Reverse(), SortedIds("-id"));
    }

    [Fact]
    public void ApplySort_TiedValues_BreakTiesWithDefault()
    {
        // Price = Id % 5: ties are broken by Id ascending.
        Assert.Equal(new[] { 5, 10, 1, 6, 11, 2, 7, 12, 3, 8, 4, 9 }, SortedIds("price"));
    }

    [Fact]
    public void ApplySort_MultipleFields_AppliesInOrder()
    {
        Assert.Equal(new[] { 9, 4, 8, 3, 12, 7, 2, 11, 6, 1, 10, 5 }, SortedIds("-price,-id"));
    }

    [Fact]
    public void ApplySort_IsCaseAndWhitespaceTolerant()
    {
        Assert.Equal(SortedIds("-price,-id"), SortedIds(" -PRICE , -Id "));
    }

    [Fact]
    public void ApplySort_PlusPrefix_IsAscending()
    {
        Assert.Equal(SortedIds("price"), SortedIds("+price"));
    }

    [Fact]
    public void ApplySort_UnknownField_ThrowsListingAllowedFields()
    {
        var ex = Assert.Throws<InvalidQueryRequestException>(() => SortedIds("-secret"));

        Assert.Equal("sort", ex.ParamName);
        Assert.Contains("secret", ex.Message);
        Assert.Contains("name, price, id", ex.Message);
    }

    [Theory]
    [InlineData("price,price")]
    [InlineData("price,-PRICE")]
    public void ApplySort_DuplicateField_Throws(string sort)
    {
        Assert.Throws<InvalidQueryRequestException>(() => SortedIds(sort));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("price,,name")]
    public void ApplySort_EmptySegment_Throws(string sort)
    {
        Assert.Throws<InvalidQueryRequestException>(() => SortedIds(sort));
    }

    [Theory]
    [InlineData("--price")]
    [InlineData("-+price")]
    [InlineData("+-price")]
    public void ApplySort_MoreThanOneSign_Throws(string sort)
    {
        Assert.Throws<InvalidQueryRequestException>(() => SortedIds(sort));
    }

    [Fact]
    public void ApplySort_DefaultDescending_IsUsedAndBreaksTiesDescending()
    {
        var map = new SortMap<MyEntity>().Add("price", e => e.Price).Default(e => e.Id, descending: true);

        Assert.Equal(Enumerable.Range(1, 12).Reverse(), _db.Entities.ApplySort(null, map).Select(e => e.Id));
        Assert.Equal(new[] { 10, 5, 11, 6, 1 }, _db.Entities.ApplySort("price", map).Select(e => e.Id).Take(5));
    }

    [Fact]
    public void ApplySort_DefaultKeyWithDifferentParameterName_IsNotRepeated()
    {
        var map = new SortMap<MyEntity>().Add("id", x => x.Id).Default(p => p.Id);

        var sql = _db.Entities.ApplySort("-id", map).ToQueryString();
        var orderBy = sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..];

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(orderBy, "\"Id\""));
    }

    [Fact]
    public void ApplySort_SortingByDefaultKey_DoesNotRepeatIt()
    {
        var sql = _db.Entities.ApplySort("-id", Sorts).ToQueryString();
        var orderBy = sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..];

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(orderBy, "\"Id\""));
    }

    [Fact]
    public void ApplySort_MapWithoutDefault_Throws()
    {
        var noDefault = new SortMap<MyEntity>().Add("name", e => e.Name);

        Assert.Throws<InvalidOperationException>(() => _db.Entities.ApplySort("name", noDefault));
    }

    [Fact]
    public async Task ApplySort_ResultCanBePaged()
    {
        var result = await _db.Entities.ApplySort("-price", Sorts).ToPagedListAsync(new PageRequest { PageSize = 3 });

        Assert.Equal(new[] { 4, 9, 3 }, result.Items.Select(e => e.Id));
        Assert.Equal(12, result.TotalCount);
    }
}
