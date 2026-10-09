using System.Text;
using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Pagination.Cursors;
using EntityFrameworkToolKit.Sorting;
using EntityFrameworkToolKit.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Tests;

public sealed class CursorQueryableExtensionsTests : IDisposable
{
    private const int Count = 53;
    private readonly TestDbContext _db = TestDbContext.CreateSeeded(0);

    public CursorQueryableExtensionsTests() => _db.SeedCursorEntities(Count);

    public void Dispose() => _db.Dispose();

    // Every sort ends with a unique key (Id), as cursor paging requires.
    private static readonly Dictionary<string, Func<IQueryable<CursorEntity>, IQueryable<CursorEntity>>> Sorts = new()
    {
        ["id"] = q => q.OrderBy(e => e.Id),
        ["id desc"] = q => q.OrderByDescending(e => e.Id),
        ["group"] = q => q.OrderBy(e => e.Group).ThenBy(e => e.Id),
        ["group desc, id"] = q => q.OrderByDescending(e => e.Group).ThenBy(e => e.Id),
        ["group, id desc"] = q => q.OrderBy(e => e.Group).ThenByDescending(e => e.Id),
        ["long"] = q => q.OrderBy(e => e.Big),
        ["string"] = q => q.OrderBy(e => e.Name).ThenBy(e => e.Id),
        ["string desc"] = q => q.OrderByDescending(e => e.Name).ThenByDescending(e => e.Id),
        ["datetime"] = q => q.OrderBy(e => e.Created).ThenBy(e => e.Id),
        ["guid"] = q => q.OrderBy(e => e.Code).ThenBy(e => e.Id),
        ["enum desc"] = q => q.OrderByDescending(e => e.Status).ThenBy(e => e.Id),
        ["double"] = q => q.OrderBy(e => e.Score).ThenBy(e => e.Id),
        ["three keys"] = q => q.OrderByDescending(e => e.Status).ThenBy(e => e.Name).ThenByDescending(e => e.Id),
        ["computed"] = q => q.OrderBy(e => e.Group * 10 + (int)e.Status).ThenBy(e => e.Id),
    };

    public static TheoryData<string> SortNames => new(Sorts.Keys);

    [Theory]
    [MemberData(nameof(SortNames))]
    public async Task Forward_WalksAllRowsOnceInOrder(string sort)
    {
        var expected = await Sorts[sort](_db.CursorEntities).Select(e => e.Id).ToListAsync();

        var pages = await WalkForwardAsync(Sorts[sort], pageSize: 7);

        Assert.Equal(expected, pages.SelectMany(p => p.Items).Select(e => e.Id));
        Assert.Equal(8, pages.Count);
        Assert.All(pages.SkipLast(1), p => Assert.Equal(7, p.Items.Count));
        Assert.False(pages[0].HasPreviousPage);
        Assert.All(pages.Skip(1), p => Assert.True(p.HasPreviousPage));
        Assert.All(pages.SkipLast(1), p => Assert.True(p.HasNextPage));
        Assert.False(pages[^1].HasNextPage);
    }

    [Theory]
    [MemberData(nameof(SortNames))]
    public async Task Backward_WalksAllRowsOnceInOrder(string sort)
    {
        var expected = await Sorts[sort](_db.CursorEntities).Select(e => e.Id).ToListAsync();
        var last = (await WalkForwardAsync(Sorts[sort], pageSize: 7))[^1];

        var pages = new List<CursorPagedResult<CursorEntity>> { last };
        while (pages[0].PreviousCursor is { } before)
            pages.Insert(0, await Sorts[sort](_db.CursorEntities).ToCursorPagedListAsync(new CursorRequest { Before = before, PageSize = 7 }));

        Assert.Equal(expected, pages.SelectMany(p => p.Items).Select(e => e.Id));
        Assert.All(pages.SkipLast(1), p => Assert.True(p.HasNextPage));
        Assert.False(pages[0].HasPreviousPage);
    }

    [Fact]
    public async Task NextThenPrevious_ReturnsSamePage()
    {
        var query = Sorts["string"](_db.CursorEntities);
        var first = await query.ToCursorPagedListAsync(new CursorRequest { PageSize = 10 });
        var second = await query.ToCursorPagedListAsync(new CursorRequest { After = first.NextCursor, PageSize = 10 });

        var back = await query.ToCursorPagedListAsync(new CursorRequest { Before = second.PreviousCursor, PageSize = 10 });

        Assert.Equal(first.Items.Select(e => e.Id), back.Items.Select(e => e.Id));
        Assert.False(back.HasPreviousPage);
        Assert.True(back.HasNextPage);
        Assert.Equal(
            second.Items.Select(e => e.Id),
            (await query.ToCursorPagedListAsync(new CursorRequest { After = back.NextCursor, PageSize = 10 })).Items.Select(e => e.Id));
    }

    [Fact]
    public async Task EmptyPage_HasNoCursors()
    {
        var query = Sorts["id"](_db.CursorEntities);
        var first = await query.ToCursorPagedListAsync(new CursorRequest { PageSize = 3 });
        var second = await query.ToCursorPagedListAsync(new CursorRequest { After = first.NextCursor, PageSize = 3 });
        _db.CursorEntities.RemoveRange(_db.CursorEntities.Where(e => e.Id <= 3));
        await _db.SaveChangesAsync();

        var empty = await query.ToCursorPagedListAsync(new CursorRequest { Before = second.PreviousCursor, PageSize = 3 });

        Assert.Empty(empty.Items);
        Assert.False(empty.HasPreviousPage);
        Assert.False(empty.HasNextPage);
    }

    [Fact]
    public async Task InsertBeforeCursor_DoesNotDuplicateOrSkip()
    {
        var query = Sorts["id desc"](_db.CursorEntities);
        var first = await query.ToCursorPagedListAsync(new CursorRequest { PageSize = 10 });
        _db.CursorEntities.Add(new CursorEntity { Id = Count + 1, Name = "new" });
        await _db.SaveChangesAsync();

        var second = await query.ToCursorPagedListAsync(new CursorRequest { After = first.NextCursor, PageSize = 10 });

        Assert.Equal(Enumerable.Range(Count - 19, 10).Reverse(), second.Items.Select(e => e.Id));
    }

    [Fact]
    public async Task Page_RunsOneParameterizedQuery()
    {
        var query = Sorts["string"](_db.CursorEntities);
        var first = await query.ToCursorPagedListAsync(new CursorRequest { PageSize = 5 });
        var lastName = first.Items[^1].Name;
        _db.ExecutedCommands.Clear();

        await query.ToCursorPagedListAsync(new CursorRequest { After = first.NextCursor, PageSize = 5 });

        var sql = Assert.Single(_db.ExecutedCommands);
        Assert.DoesNotContain($"'{lastName}'", sql);
        Assert.Contains("WHERE", sql);
        Assert.Contains(">=", sql); // the leading "first key >= value" range that lets the database seek an index
        Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("COUNT", sql);
    }

    [Fact]
    public async Task WorksWithApplySortAndEfOperatorsAfterTheOrdering()
    {
        var map = new SortMap<CursorEntity>().Add("name", e => e.Name).Add("group", e => e.Group).Default(e => e.Id);
        IQueryable<CursorEntity> Query() => _db.CursorEntities.ApplySort("-group,name", map).AsNoTracking().TagWith("feed");
        var expected = await Query().Select(e => e.Id).ToListAsync();

        var pages = await WalkForwardAsync(_ => Query(), pageSize: 10);

        Assert.Equal(expected, pages.SelectMany(p => p.Items).Select(e => e.Id));
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PageSize_DefaultsAndClampsWithOptions()
    {
        var query = Sorts["id"](_db.CursorEntities);

        Assert.Equal(20, (await query.ToCursorPagedListAsync(new CursorRequest())).Items.Count);
        var clamped = await query.ToCursorPagedListAsync(new CursorRequest { PageSize = 500 }, new PagingOptions(5, 8));
        Assert.Equal(8, clamped.PageSize);
        Assert.Equal(8, clamped.Items.Count);
    }

    public static TheoryData<string> InvalidCursors()
    {
        var fingerprint = CursorOrdering<CursorEntity>.From(Sorts["group"](Array.Empty<CursorEntity>().AsQueryable())).Fingerprint;
        string Craft(string json) => CursorCodec.ToBase64Url(Encoding.UTF8.GetBytes(json));
        return new TheoryData<string>
        {
            "not a cursor!",
            "",
            Craft("not json"),
            Craft("[]"),
            Craft("{}"),
            Craft($$"""{"s":"{{fingerprint}}","v":[1]}"""),
            Craft($$"""{"s":"{{fingerprint}}","v":[1,2,3]}"""),
            Craft($$"""{"s":"{{fingerprint}}","v":["1",2]}"""),
            Craft($$"""{"s":"{{fingerprint}}","v":[1.5,2]}"""),
            Craft($$"""{"s":"{{fingerprint}}","v":[null,2]}"""),
            Craft($$"""{"s":"{{fingerprint}}","v":[1,2],"x":1}"""),
            Craft($$"""{"s":"other","v":[1,2]}"""),
            new string('a', CursorCodec.MaxLength + 1),
        };
    }

    [Theory]
    [MemberData(nameof(InvalidCursors))]
    public async Task InvalidCursor_ThrowsNamingTheParameter(string cursor)
    {
        var query = Sorts["group"](_db.CursorEntities);
        if (cursor.Length == 0)
            cursor = " ";

        var after = await Assert.ThrowsAsync<InvalidQueryRequestException>(() => query.ToCursorPagedListAsync(new CursorRequest { After = cursor }));
        var before = await Assert.ThrowsAsync<InvalidQueryRequestException>(() => query.ToCursorPagedListAsync(new CursorRequest { Before = cursor }));

        Assert.Equal("after", after.ParamName);
        Assert.Equal("before", before.ParamName);
        Assert.Empty(_db.ExecutedCommands);
    }

    [Fact]
    public async Task CursorFromAnotherSort_IsRejected()
    {
        var byName = await Sorts["string"](_db.CursorEntities).ToCursorPagedListAsync(new CursorRequest { PageSize = 5 });

        var ex = await Assert.ThrowsAsync<InvalidQueryRequestException>(() =>
            Sorts["string desc"](_db.CursorEntities).ToCursorPagedListAsync(new CursorRequest { After = byName.NextCursor }));

        Assert.Equal("after", ex.ParamName);
        Assert.Contains("different sort", ex.Message);
    }

    [Fact]
    public async Task BothCursors_Throw()
    {
        var first = await Sorts["id"](_db.CursorEntities).ToCursorPagedListAsync(new CursorRequest { PageSize = 5 });

        var ex = await Assert.ThrowsAsync<InvalidQueryRequestException>(() => Sorts["id"](_db.CursorEntities)
            .ToCursorPagedListAsync(new CursorRequest { After = first.NextCursor, Before = first.NextCursor }));

        Assert.Equal("before", ex.ParamName);
    }

    public static TheoryData<string> MisusedQueries => new()
    {
        "unordered",
        "where after order",
        "select after order",
        "nullable key",
        "nullable reference key",
        "unsupported key type",
    };

    [Theory]
    [MemberData(nameof(MisusedQueries))]
    public async Task MisusedQuery_ThrowsInvalidOperation(string misuse)
    {
        IQueryable<object> query = misuse switch
        {
            "unordered" => _db.CursorEntities,
            "where after order" => _db.CursorEntities.OrderBy(e => e.Id).Where(e => e.Group > 0),
            "select after order" => _db.CursorEntities.OrderBy(e => e.Id).Select(e => new { e.Id }),
            "nullable key" => _db.CursorEntities.OrderBy(e => e.Optional).ThenBy(e => e.Id),
            "nullable reference key" => _db.CursorEntities.OrderBy(e => e.Nickname).ThenBy(e => e.Id),
            "unsupported key type" => _db.CursorEntities.OrderBy(e => e.Group > 1).ThenBy(e => e.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(misuse)),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToCursorPagedListAsync(new CursorRequest()));
        Assert.Empty(_db.ExecutedCommands);
    }

    private async Task<List<CursorPagedResult<CursorEntity>>> WalkForwardAsync(
        Func<IQueryable<CursorEntity>, IQueryable<CursorEntity>> sort, int pageSize)
    {
        var pages = new List<CursorPagedResult<CursorEntity>>();
        string? after = null;
        do
        {
            var page = await sort(_db.CursorEntities).ToCursorPagedListAsync(new CursorRequest { After = after, PageSize = pageSize });
            pages.Add(page);
            after = page.NextCursor;
            Assert.True(pages.Count <= Count, "The walk did not end.");
        }
        while (after is not null);

        return pages;
    }
}
