using EntityFrameworkToolKit.Pagination;
using EntityFrameworkToolKit.Tests.TestHelpers;
using MockQueryable.NSubstitute;

namespace EntityFrameworkToolKit.Tests;

public class PaginationIQueryableHelperTests
{
    [Theory]
    [InlineData(1, 2, 2)]
    [InlineData(-4, -6, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(null, 10, 0)]
    [InlineData(null, null, 0)]
    [InlineData(1, null, 0)]
    public async Task AddPagination_ReturnsRightAmountOfItems(int? page, int? size, int expected)
    {
        // Arrange
        // mock a DbSet
        var mock = GetTestData().AsQueryable().BuildMockDbSet();

        // Act
        var result = await mock.AddPagination(page, size);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected,result.Items.Count());
        Assert.IsType<PaginatedIEnumerable<MyEntity>>(result);
    }

    [Theory]
    [InlineData(1, 2, 2)]
    [InlineData(-4, -6, 50)]
    [InlineData(2, 10, 10)]
    [InlineData(null, 10, 50)]
    [InlineData(null, null, 50)]
    [InlineData(1, null, 50)]
    public async Task AddOptionalPagination_ReturnsRightAmountOfItems(int? page, int? size, int expected)
    {
        // Arrange
        // mock a DbSet
        var mock = GetTestData().AsQueryable().BuildMockDbSet();

        // Act
        var result = await mock.AddOptionalPagination(page, size);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected,result.Items.Count());
        Assert.IsType<PaginatedIEnumerable<MyEntity>>(result);
    }

    [Fact]
    public async Task AddPagination_ValidInput_ReturnsCorrectPage()
    {
        // Arrange
        // mock a DbSet
        var data = GetTestData();
        var mock = data.AsQueryable().BuildMockDbSet();
        var expected = data.Skip(10).Take(10);
        var page = 2;
        var size = 10;
            
        // Act
        var result = await mock.AddPagination(page, size);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected,result.Items);
        Assert.Equal(size,result.Items.Count());
        Assert.Equal(data.Count,result.Total);
        Assert.IsType<PaginatedIEnumerable<MyEntity>>(result);
    }
    
    
    [Fact]
    public async Task AddOptionalPagination_ValidInput_ReturnsCorrectPage()
    {
        // Arrange
        // mock a DbSet
        var data = GetTestData();
        var mock = data.AsQueryable().BuildMockDbSet();
        var expected = data.Skip(10).Take(10);
        var page = 2;
        var size = 10;
            
        // Act
        var result = await mock.AddOptionalPagination(page, size);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected,result.Items);
        Assert.Equal(size,result.Items.Count());
        Assert.Equal(data.Count,result.Total);
        Assert.IsType<PaginatedIEnumerable<MyEntity>>(result);
    }
    
    private List<MyEntity> GetTestData()
    {
        // Replace with your test data creation logic
        return Enumerable.Range(1, 50)
            .Select(i => new MyEntity { Id = i, Name = $"Entity {i}" })
            .ToList();
    }
   
}
