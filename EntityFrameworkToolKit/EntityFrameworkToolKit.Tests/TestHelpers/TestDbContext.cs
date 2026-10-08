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

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}
