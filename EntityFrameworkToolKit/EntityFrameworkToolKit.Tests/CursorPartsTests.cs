using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Pagination.Cursors;
using EntityFrameworkToolKit.Tests.TestHelpers;

namespace EntityFrameworkToolKit.Tests;

public class CursorPartsTests
{
    private static readonly IQueryable<CursorEntity> Source = Array.Empty<CursorEntity>().AsQueryable();

    [Fact]
    public void Codec_RoundTripsEveryKeyType()
    {
        var values = new object[]
        {
            42, 10_000_000_000L, "Name ✓", new DateTime(2024, 5, 6, 7, 8, 9, 123), new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2)),
            new DateOnly(2024, 5, 6), new TimeOnly(7, 8, 9), Guid.NewGuid(), Status.Archived, 0.1 + 0.2, 12.345m, 1.5f,
        };
        var types = values.Select(v => v.GetType()).ToList();

        var cursor = CursorCodec.Encode("fp", values, types);

        Assert.Equal(values, CursorCodec.Decode(cursor, "fp", types, "after"));
        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.DoesNotContain('=', cursor);
    }

    [Fact]
    public void Fingerprint_IgnoresParameterNamesButNotKeysOrDirections()
    {
        string Fingerprint(IQueryable<CursorEntity> query) => CursorOrdering<CursorEntity>.From(query).Fingerprint;

        var reference = Fingerprint(Source.OrderBy(e => e.Group).ThenBy(e => e.Id));

        Assert.Equal(reference, Fingerprint(Source.OrderBy(x => x.Group).ThenBy(y => y.Id)));
        Assert.NotEqual(reference, Fingerprint(Source.OrderBy(e => e.Group).ThenByDescending(e => e.Id)));
        Assert.NotEqual(reference, Fingerprint(Source.OrderBy(e => e.Status).ThenBy(e => e.Id)));
        Assert.NotEqual(reference, Fingerprint(Source.OrderBy(e => e.Id).ThenBy(e => e.Group)));
    }

    [Fact]
    public void Ordering_ReadsKeysInSignificanceOrder()
    {
        var ordering = CursorOrdering<CursorEntity>.From(Source.OrderByDescending(e => e.Status).ThenBy(e => e.Name).ThenByDescending(e => e.Id));

        Assert.Equal(new[] { typeof(Status), typeof(string), typeof(int) }, ordering.Keys.Select(k => k.Type));
        Assert.Equal(new[] { true, false, true }, ordering.Keys.Select(k => k.Descending));
        Assert.Equal(new object[] { Status.Active, "n", 7 }, ordering.ReadKeys(new CursorEntity { Id = 7, Name = "n", Status = Status.Active }));
    }

    [Fact]
    public void Ordering_NullKeyValue_Throws()
    {
        var ordering = CursorOrdering<CursorEntity>.From(Source.OrderBy(e => e.Name).ThenBy(e => e.Id));

        Assert.Throws<InvalidOperationException>(() => ordering.ReadKeys(new CursorEntity { Name = null! }));
    }

    [Theory]
    [InlineData(null, null, null, 20, null, false, "after")]
    [InlineData(5, "a", null, 5, "a", false, "after")]
    [InlineData(500, null, "b", 100, "b", true, "before")]
    [InlineData(null, "", "b", 20, "b", true, "before")]
    [InlineData(null, "a", "", 20, "a", false, "after")]
    public void Request_Normalize(
        int? pageSize, string? after, string? before, int expectedSize, string? expectedCursor, bool expectedBackward, string expectedParameter)
    {
        var request = new CursorRequest { PageSize = pageSize, After = after, Before = before };

        Assert.Equal((expectedSize, expectedCursor, expectedBackward, expectedParameter), request.Normalize(PagingOptions.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Request_InvalidPageSize_Throws(int pageSize)
    {
        var ex = Assert.Throws<InvalidQueryRequestException>(() => new CursorRequest { PageSize = pageSize }.Normalize(PagingOptions.Default));

        Assert.Equal("pageSize", ex.ParamName);
    }

    [Fact]
    public void Result_ValidatesAndDerivesFlags()
    {
        Assert.Throws<ArgumentNullException>(() => new CursorPagedResult<int>(null!, 1, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CursorPagedResult<int>(new[] { 1 }, 0, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CursorPagedResult<int>(new[] { 1, 2 }, 1, null, null));

        var result = new CursorPagedResult<int>(new[] { 1, 2 }, 5, "next", null);

        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void Result_Map_KeepsCursors()
    {
        var mapped = new CursorPagedResult<int>(new[] { 1, 2 }, 5, "next", "prev").Map(i => i * 10);

        Assert.Equal(new[] { 10, 20 }, mapped.Items);
        Assert.Equal((5, "next", "prev"), (mapped.PageSize, mapped.NextCursor, mapped.PreviousCursor));
    }
}
