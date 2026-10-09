namespace EntityFrameworkToolKit.Tests.TestHelpers;

public enum Status
{
    Draft,
    Active,
    Archived,
}

/// <summary>One column per cursor key type, with plenty of ties in <see cref="Group"/> and <see cref="Status"/>.</summary>
public class CursorEntity
{
    public int Id { get; set; }
    public int Group { get; set; }
    public long Big { get; set; }
    public string Name { get; set; } = "";
    public DateTime Created { get; set; }
    public Guid Code { get; set; }
    public Status Status { get; set; }
    public double Score { get; set; }
    public int? Optional { get; set; }
    public string? Nickname { get; set; }
    public decimal Amount { get; set; }
}
