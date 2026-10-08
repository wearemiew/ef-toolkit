using EntityFrameworkToolKit.Filtering;
using EntityFrameworkToolKit.Tests.TestHelpers;

namespace EntityFrameworkToolKit.Tests;

public sealed class WhereIfTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContext.CreateSeeded(10);

    public void Dispose() => _db.Dispose();

    [Fact]
    public void WhereIf_ConditionTrue_AppliesPredicate()
    {
        var ids = _db.Entities.WhereIf(true, e => e.Id > 7).Select(e => e.Id).ToArray();

        Assert.Equal(new[] { 8, 9, 10 }, ids);
    }

    [Fact]
    public void WhereIf_ConditionFalse_ReturnsQueryUnchanged()
    {
        IQueryable<MyEntity> query = _db.Entities;

        Assert.Same(query, query.WhereIf(false, e => e.Id > 7));
    }

    [Fact]
    public void WhereIf_Chained_CombinesOnlyActiveFilters()
    {
        string? search = "Entity 1";
        int? minPrice = null;

        var ids = _db.Entities
            .WhereIf(search is not null, e => e.Name.StartsWith(search!))
            .WhereIf(minPrice is not null, e => e.Price >= minPrice!.Value)
            .Select(e => e.Id)
            .ToArray();

        Assert.Equal(new[] { 1, 10 }, ids);
    }
}
