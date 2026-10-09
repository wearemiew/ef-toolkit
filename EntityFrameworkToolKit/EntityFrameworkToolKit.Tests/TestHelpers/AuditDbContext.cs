using EntityFrameworkToolKit.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkToolKit.Tests.TestHelpers;

public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Audited and soft-deletable, with an owned address stored in the same row.</summary>
public class Order : IUserAuditable, IUserSoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Address ShipTo { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

public class Address
{
    public string Street { get; set; } = "";
}

/// <summary>Timestamps only, no users, no soft delete.</summary>
public class Stamp : IAuditable
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Soft-deletable with a pre-existing (tenant) query filter.</summary>
public class Note : ISoftDeletable
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>A soft-deletable principal with one dependent of each cascade kind.</summary>
public class Parent : ISoftDeletable
{
    public int Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<OptionalChild> Optionals { get; set; } = new();
    public List<RequiredChild> Requireds { get; set; } = new();
    public List<SoftChild> SoftChildren { get; set; } = new();
}

/// <summary>Optional FK: EF's default (ClientSetNull) nulls it when the parent is removed.</summary>
public class OptionalChild
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
}

/// <summary>Required FK: EF's default (Cascade) deletes it when the parent is removed.</summary>
public class RequiredChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public List<GrandChild> GrandChildren { get; set; } = new();
    public List<SoftGrandChild> SoftGrandChildren { get; set; } = new();
}

/// <summary>A plain row two levels below a soft-deletable parent.</summary>
public class GrandChild
{
    public int Id { get; set; }
    public int RequiredChildId { get; set; }
}

/// <summary>A soft-deletable row under a plain parent.</summary>
public class SoftGrandChild : ISoftDeletable
{
    public int Id { get; set; }
    public int RequiredChildId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>Required FK and soft-deletable: should be soft-deleted with the parent.</summary>
public class SoftChild : ISoftDeletable
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>No auditing at all.</summary>
public class Plain
{
    public int Id { get; set; }
}

/// <summary>Soft-deletable with an optional link to a plain row that must block the plain row's delete (Restrict).</summary>
public class SoftRestricted : ISoftDeletable
{
    public int Id { get; set; }
    public int? PlainId { get; set; }
    public Plain? Plain { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>Soft-deletable with an optional, non-cascading link to a plain row.</summary>
public class SoftLinked : ISoftDeletable
{
    public int Id { get; set; }
    public int? PlainId { get; set; }
    public Plain? Plain { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class AuditDbContext : DbContext
{
    public const int CurrentTenant = 1;

    private AuditDbContext(DbContextOptions<AuditDbContext> options, List<string> commands) : base(options)
    {
        ExecutedCommands = commands;
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Stamp> Stamps => Set<Stamp>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Plain> Plains => Set<Plain>();
    public DbSet<SoftLinked> SoftLinked => Set<SoftLinked>();
    public DbSet<SoftRestricted> SoftRestricted => Set<SoftRestricted>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<OptionalChild> OptionalChildren => Set<OptionalChild>();
    public DbSet<RequiredChild> RequiredChildren => Set<RequiredChild>();
    public DbSet<SoftChild> SoftChildren => Set<SoftChild>();
    public DbSet<GrandChild> GrandChildren => Set<GrandChild>();
    public DbSet<SoftGrandChild> SoftGrandChildren => Set<SoftGrandChild>();

    public List<string> ExecutedCommands { get; }

    /// <summary>A context over <paramref name="connection"/> with auditing; share the connection to read back what was saved.</summary>
    public static AuditDbContext Create(SqliteConnection connection, TimeProvider? clock = null, Func<string?>? user = null)
    {
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connection)
            .UseAuditing(user, clock)
            .LogTo(commands.Add, new[] { RelationalEventId.CommandExecuted })
            .Options;
        var context = new AuditDbContext(options, commands);
        context.Database.EnsureCreated();
        return context;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().OwnsOne(o => o.ShipTo);
        modelBuilder.Entity<Note>().HasQueryFilter(n => n.TenantId == CurrentTenant);
        modelBuilder.Entity<SoftRestricted>().HasOne(r => r.Plain).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.ApplySoftDeleteQueryFilters();
    }
}
