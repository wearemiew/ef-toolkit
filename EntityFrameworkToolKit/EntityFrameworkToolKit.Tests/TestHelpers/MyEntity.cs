namespace EntityFrameworkToolKit.Tests.TestHelpers;

public class MyEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Seeded as Id % 5, so many rows share a price (useful for tie-breaking tests).</summary>
    public int Price { get; set; }
}