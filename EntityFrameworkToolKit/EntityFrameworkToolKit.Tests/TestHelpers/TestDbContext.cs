using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkToolKit.Tests.TestHelpers;

public sealed class TestDbContext : DbContext
{
    private readonly SqliteConnection _connection;

    private TestDbContext(SqliteConnection connection, DbContextOptions<TestDbContext> options, List<string> commands)
        : base(options)
    {
        _connection = connection;
        ExecutedCommands = commands;
    }

    public DbSet<MyEntity> Entities => Set<MyEntity>();

    public DbSet<CursorEntity> CursorEntities => Set<CursorEntity>();

    /// <summary>SQL commands executed since seeding finished.</summary>
    public List<string> ExecutedCommands { get; }

    /// <summary>
    /// Creates a context over a fresh in-memory SQLite database seeded with <paramref name="count"/> entities (Id 1..count).
    /// </summary>
    public static TestDbContext CreateSeeded(int count = 50)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .LogTo(commands.Add, new[] { RelationalEventId.CommandExecuted })
            .Options;

        var context = new TestDbContext(connection, options, commands);
        context.Database.EnsureCreated();
        context.Entities.AddRange(Enumerable.Range(1, count).Select(i => new MyEntity { Id = i, Name = $"Entity {i}", Price = i % 5 }));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        commands.Clear();
        return context;
    }

    /// <summary>
    /// Adds <paramref name="count"/> cursor entities (Id 1..count) whose other columns repeat, so sorts have ties.
    /// </summary>
    public void SeedCursorEntities(int count)
    {
        CursorEntities.AddRange(Enumerable.Range(1, count).Select(i => new CursorEntity
        {
            Id = i,
            Group = i % 4,
            Big = 10_000_000_000L + (i * 7 % count),
            Name = $"Name {i % 6:00}",
            Created = new DateTime(2024, 1, 1).AddHours(i % 9),
            Code = new Guid($"{i * 37 % 101:00000000}-0000-0000-0000-000000000000"),
            Status = (Status)(i % 3),
            Score = (i % 5) / 4.0,
            Optional = i,
            Amount = i,
        }));
        SaveChanges();
        ChangeTracker.Clear();
        ExecutedCommands.Clear();
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}
