using EntityFrameworkToolKit.Sorting;
using EntityFrameworkToolKit.Tests.TestHelpers;

namespace EntityFrameworkToolKit.Tests;

public class SortMapTests
{
    [Fact]
    public void Fields_ListsRegisteredNamesInOrder()
    {
        var map = new SortMap<MyEntity>().Add("name", e => e.Name).Add("price", e => e.Price);

        Assert.Equal(new[] { "name", "price" }, map.Fields);
    }

    [Fact]
    public void Fields_CannotBeMutatedByCallers()
    {
        var map = new SortMap<MyEntity>().Add("name", e => e.Name);

        Assert.IsNotType<List<string>>(map.Fields);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)map.Fields).Add("secret"));
    }

    [Fact]
    public void Add_DuplicateNameIgnoringCase_Throws()
    {
        var map = new SortMap<MyEntity>().Add("name", e => e.Name);

        Assert.Throws<ArgumentException>(() => map.Add("NAME", e => e.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-name")]
    [InlineData("+name")]
    [InlineData("a,b")]
    [InlineData(" name")]
    public void Add_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new SortMap<MyEntity>().Add(name, e => e.Name));
    }

    [Fact]
    public void Default_CalledTwice_Throws()
    {
        var map = new SortMap<MyEntity>().Default(e => e.Id);

        Assert.Throws<InvalidOperationException>(() => map.Default(e => e.Name));
    }
}
