using EntityFrameworkToolKit.Querying;
using EntityFrameworkToolKit.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Tests;

public sealed class NotFoundQueryableExtensionsTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContext.CreateSeeded(5);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FirstOrNotFoundAsync_ReturnsFirstItem()
    {
        var entity = await _db.Entities.OrderBy(e => e.Id).FirstOrNotFoundAsync();

        Assert.Equal(1, entity.Id);
    }

    [Fact]
    public async Task FirstOrNotFoundAsync_WithPredicate_ReturnsMatch()
    {
        var entity = await _db.Entities.OrderBy(e => e.Id).FirstOrNotFoundAsync(e => e.Id > 3);

        Assert.Equal(4, entity.Id);
    }

    [Fact]
    public async Task FirstOrNotFoundAsync_Empty_ThrowsWithEntityName()
    {
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _db.Entities.FirstOrNotFoundAsync(e => e.Id > 99));

        Assert.Equal("MyEntity", ex.EntityName);
        Assert.Null(ex.Key);
        Assert.Equal("No MyEntity matched the query.", ex.Message);
    }

    [Fact]
    public async Task FirstOrNotFoundAsync_Projection_NamesTheQueriedEntity()
    {
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _db.Entities.Where(e => e.Id > 99).Select(e => e.Id).FirstOrNotFoundAsync());

        Assert.Equal("No MyEntity matched the query.", ex.Message);
    }

    [Fact]
    public async Task SingleOrNotFoundAsync_AnonymousProjection_NamesTheQueriedEntity()
    {
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _db.Entities.OrderBy(e => e.Id).Select(e => new { e.Id, e.Name }).SingleOrNotFoundAsync(e => e.Id > 99));

        Assert.Equal("MyEntity", ex.EntityName);
    }

    [Fact]
    public async Task FirstOrNotFoundAsync_EmptyValueTypeProjection_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _db.Entities.Where(e => e.Id > 99).Select(e => e.Id).FirstOrNotFoundAsync());
    }

    [Fact]
    public async Task FirstOrNotFoundAsync_NullElement_IsReturned()
    {
        var name = await _db.Entities.Where(e => e.Id == 1).Select(e => (string?)null).FirstOrNotFoundAsync();

        Assert.Null(name);
    }

    [Fact]
    public async Task SingleOrNotFoundAsync_ZeroValue_IsReturned()
    {
        var price = await _db.Entities.Where(e => e.Id == 5).Select(e => e.Price).SingleOrNotFoundAsync();

        Assert.Equal(0, price);
    }

    [Fact]
    public async Task SingleOrNotFoundAsync_ReturnsOnlyMatch()
    {
        var entity = await _db.Entities.SingleOrNotFoundAsync(e => e.Id == 2);

        Assert.Equal(2, entity.Id);
    }

    [Fact]
    public async Task SingleOrNotFoundAsync_NoMatch_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _db.Entities.Where(e => e.Id == 99).SingleOrNotFoundAsync());
    }

    [Fact]
    public async Task SingleOrNotFoundAsync_SeveralMatches_ThrowsInvalidOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.Entities.SingleOrNotFoundAsync(e => e.Id > 1));
    }

    [Fact]
    public async Task FindOrNotFoundAsync_ReturnsEntity()
    {
        var entity = await _db.Entities.FindOrNotFoundAsync(3);

        Assert.Equal(3, entity.Id);
    }

    [Fact]
    public async Task FindOrNotFoundAsync_Tracked_ReturnsInstanceWithoutQuery()
    {
        var tracked = await _db.Entities.FirstAsync(e => e.Id == 3);
        _db.ExecutedCommands.Clear();

        var entity = await _db.Entities.FindOrNotFoundAsync(3);

        Assert.Same(tracked, entity);
        Assert.Empty(_db.ExecutedCommands);
    }

    [Fact]
    public async Task FindOrNotFoundAsync_Missing_ThrowsWithKey()
    {
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _db.Entities.FindOrNotFoundAsync(42));

        Assert.Equal("MyEntity", ex.EntityName);
        Assert.Equal(42, ex.Key);
        Assert.Equal("MyEntity 42 was not found.", ex.Message);
    }

    [Fact]
    public async Task FindOrNotFoundAsync_KeyValues_Missing_ThrowsWithKey()
    {
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => _db.Entities.FindOrNotFoundAsync(new object?[] { 42 }));

        Assert.Equal(42, ex.Key);
    }

    [Fact]
    public void EntityNotFoundException_CompositeKey_FormatsAllParts()
    {
        var ex = new EntityNotFoundException("OrderLine", new object?[] { 7, "A" });

        Assert.Equal("OrderLine (7, A) was not found.", ex.Message);
    }

    [Fact]
    public void Arguments_Null_Throw()
    {
        IQueryable<MyEntity> nullQuery = null!;

        Assert.Throws<ArgumentNullException>(() => { _ = nullQuery.FirstOrNotFoundAsync(); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.Entities.FirstOrNotFoundAsync(null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.Entities.SingleOrNotFoundAsync(null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.Entities.FindOrNotFoundAsync((object)null!); });
        Assert.Throws<ArgumentException>(() => new EntityNotFoundException(""));
    }
}
