using EntityFrameworkToolKit.Auditing;
using EntityFrameworkToolKit.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Tests;

public sealed class AuditingTests : IDisposable
{
    private static readonly DateTime T1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = T1.AddHours(1);
    private static readonly DateTime T3 = T1.AddHours(2);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeTimeProvider _clock = new(T1);
    private string? _user = "alice";

    public AuditingTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private AuditDbContext Db() => AuditDbContext.Create(_connection, _clock, () => _user);

    // Reads what was saved, bypassing filters and any tracked state.
    private Order Saved(int id)
    {
        using var db = Db();
        return db.Orders.IgnoreQueryFilters().AsNoTracking().Single(o => o.Id == id);
    }

    private static async Task SaveAsync(DbContext db, bool async)
    {
        if (async)
            await db.SaveChangesAsync();
        else
            db.SaveChanges();
    }

    private async Task<int> InsertOrderAsync()
    {
        using var db = Db();
        var order = new Order { Name = "first", ShipTo = new Address { Street = "Main St" } };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Insert_SetsCreatedAndUpdated(bool async)
    {
        using var db = Db();
        db.Orders.Add(new Order { Id = 1, Name = "first" });

        await SaveAsync(db, async);

        var saved = Saved(1);
        Assert.Equal((T1, T1, "alice", "alice"), (saved.CreatedAt, saved.UpdatedAt, saved.CreatedBy, saved.UpdatedBy));
        Assert.False(saved.IsDeleted);
        Assert.Null(saved.DeletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_SetsUpdatedAndProtectsCreated(bool async)
    {
        var id = await InsertOrderAsync();
        _clock.Now = T2;
        _user = "bob";

        using (var db = Db())
        {
            var order = db.Orders.Single(o => o.Id == id);
            order.Name = "renamed";
            order.CreatedAt = T3;
            order.CreatedBy = "mallory";
            await SaveAsync(db, async);

            Assert.Equal((T1, "alice"), (order.CreatedAt, order.CreatedBy));
        }

        var saved = Saved(id);
        Assert.Equal("renamed", saved.Name);
        Assert.Equal((T1, "alice", T2, "bob"), (saved.CreatedAt, saved.CreatedBy, saved.UpdatedAt, saved.UpdatedBy));
    }

    [Fact]
    public async Task UnchangedEntity_IsNotStamped()
    {
        var id = await InsertOrderAsync();
        _clock.Now = T2;

        using (var db = Db())
        {
            _ = db.Orders.Single(o => o.Id == id);
            db.ExecutedCommands.Clear();
            await db.SaveChangesAsync();
            Assert.Empty(db.ExecutedCommands);
        }

        Assert.Equal(T1, Saved(id).UpdatedAt);
    }

    [Fact]
    public async Task TimestampsOnlyEntity_IsAudited()
    {
        using (var db = Db())
        {
            db.Stamps.Add(new Stamp { Id = 1, Value = "a" });
            await db.SaveChangesAsync();
        }

        _clock.Now = T2;
        using (var db = Db())
        {
            db.Stamps.Single().Value = "b";
            await db.SaveChangesAsync();
        }

        using var read = Db();
        var stamp = read.Stamps.AsNoTracking().Single();
        Assert.Equal((T1, T2), (stamp.CreatedAt, stamp.UpdatedAt));
    }

    [Fact]
    public async Task NoUserProvider_LeavesByNull()
    {
        using (var db = AuditDbContext.Create(_connection, _clock))
        {
            db.Orders.Add(new Order { Id = 1 });
            await db.SaveChangesAsync();
            db.Orders.Remove(db.Orders.Single());
            await db.SaveChangesAsync();
        }

        var saved = Saved(1);
        Assert.Equal(T1, saved.CreatedAt);
        Assert.Null(saved.CreatedBy);
        Assert.Null(saved.UpdatedBy);
        Assert.True(saved.IsDeleted);
        Assert.Null(saved.DeletedBy);
    }

    [Fact]
    public async Task DefaultClock_IsUtcNow()
    {
        var before = DateTime.UtcNow;
        using (var db = AuditDbContext.Create(_connection))
        {
            db.Stamps.Add(new Stamp { Id = 1 });
            await db.SaveChangesAsync();
        }

        using var read = AuditDbContext.Create(_connection);
        var created = read.Stamps.AsNoTracking().Single().CreatedAt;
        Assert.InRange(created, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remove_SoftDeletes(bool async)
    {
        var id = await InsertOrderAsync();
        _clock.Now = T2;
        _user = "bob";

        using (var db = Db())
        {
            db.Orders.Remove(db.Orders.Single(o => o.Id == id));
            db.ExecutedCommands.Clear();
            await SaveAsync(db, async);

            var sql = Assert.Single(db.ExecutedCommands);
            Assert.Contains("UPDATE", sql);
            Assert.DoesNotContain("DELETE", sql);
            Assert.DoesNotContain("\"Name\"", sql); // only the soft-delete and update stamps are written
        }

        var saved = Saved(id);
        Assert.Equal((true, (DateTime?)T2, "bob"), (saved.IsDeleted, saved.DeletedAt, saved.DeletedBy));
        Assert.Equal((T2, "bob"), (saved.UpdatedAt, saved.UpdatedBy));
        Assert.Equal((T1, "alice", "first"), (saved.CreatedAt, saved.CreatedBy, saved.Name));
    }

    [Fact]
    public async Task Remove_KeepsOwnedTypeColumns()
    {
        var id = await InsertOrderAsync();

        using (var db = Db())
        {
            db.Orders.Remove(db.Orders.Single(o => o.Id == id));
            await db.SaveChangesAsync();
        }

        Assert.Equal("Main St", Saved(id).ShipTo.Street);
    }

    [Fact]
    public async Task RemoveTwice_KeepsOriginalDeletion()
    {
        var id = await InsertOrderAsync();
        using (var db = Db())
        {
            db.Orders.Remove(db.Orders.Single(o => o.Id == id));
            await db.SaveChangesAsync();
        }

        _clock.Now = T3;
        _user = "bob";
        using (var db = Db())
        {
            db.Orders.Remove(db.Orders.IgnoreQueryFilters().Single(o => o.Id == id));
            db.ExecutedCommands.Clear();
            await db.SaveChangesAsync();
            Assert.Empty(db.ExecutedCommands);
        }

        var saved = Saved(id);
        Assert.Equal(((DateTime?)T1, "alice", T1), (saved.DeletedAt, saved.DeletedBy, saved.UpdatedAt));
    }

    [Fact]
    public async Task Remove_PlainEntity_HardDeletes()
    {
        using (var db = Db())
        {
            db.Plains.Add(new Plain { Id = 1 });
            await db.SaveChangesAsync();
            db.Plains.Remove(db.Plains.Single());
            db.ExecutedCommands.Clear();
            await db.SaveChangesAsync();
            Assert.Contains("DELETE", Assert.Single(db.ExecutedCommands));
        }

        using var read = Db();
        Assert.Empty(read.Plains);
    }

    [Fact]
    public async Task QueryFilter_HidesDeleted_AndKeepsExistingFilter()
    {
        using (var db = Db())
        {
            db.Notes.AddRange(
                new Note { Id = 1, TenantId = AuditDbContext.CurrentTenant },
                new Note { Id = 2, TenantId = AuditDbContext.CurrentTenant },
                new Note { Id = 3, TenantId = 2 });
            await db.SaveChangesAsync();
            db.Notes.Remove(db.Notes.Single(n => n.Id == 2));
            await db.SaveChangesAsync();
        }

        using var read = Db();
        Assert.Equal(new[] { 1 }, read.Notes.Select(n => n.Id));
        Assert.Equal(new[] { 1, 2, 3 }, read.Notes.IgnoreQueryFilters().OrderBy(n => n.Id).Select(n => n.Id));
        var sql = read.Notes.ToQueryString();
        Assert.Contains("\"TenantId\"", sql);
        Assert.Contains("\"IsDeleted\"", sql);
    }

    [Fact]
    public async Task Restore_ClearsTheDeletionStamp()
    {
        var id = await InsertOrderAsync();
        using (var db = Db())
        {
            db.Orders.Remove(db.Orders.Single(o => o.Id == id));
            await db.SaveChangesAsync();
        }

        _clock.Now = T2;
        using (var db = Db())
        {
            Assert.Empty(db.Orders);
            db.Orders.IgnoreQueryFilters().Single(o => o.Id == id).IsDeleted = false;
            await db.SaveChangesAsync();
        }

        var saved = Saved(id);
        Assert.Equal((false, (DateTime?)null, (string?)null), (saved.IsDeleted, saved.DeletedAt, saved.DeletedBy));
        Assert.Equal(T2, saved.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SoftDeletingAParent_UndoesEfCascades(bool async)
    {
        using (var db = Db())
        {
            db.Parents.Add(new Parent
            {
                Id = 1,
                Optionals = { new OptionalChild { Id = 10 } },
                Requireds = { new RequiredChild { Id = 20 } },
                SoftChildren = { new SoftChild { Id = 30 } },
            });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            var parent = db.Parents.Include(p => p.Optionals).Include(p => p.Requireds).Include(p => p.SoftChildren).Single();
            db.Parents.Remove(parent);
            db.ExecutedCommands.Clear();
            await SaveAsync(db, async);

            Assert.DoesNotContain(db.ExecutedCommands, sql => sql.Contains("DELETE"));
            Assert.Single(parent.Optionals);
            Assert.Single(parent.Requireds);
        }

        using var read = Db();
        Assert.True(read.Parents.IgnoreQueryFilters().AsNoTracking().Single().IsDeleted);
        Assert.Equal(1, read.OptionalChildren.AsNoTracking().Single().ParentId);   // not orphaned
        Assert.Equal(1, read.RequiredChildren.AsNoTracking().Single().ParentId);   // not hard-deleted
        var softChild = read.SoftChildren.IgnoreQueryFilters().AsNoTracking().Single();
        Assert.Equal((true, (DateTime?)T1), (softChild.IsDeleted, softChild.DeletedAt));   // the flag cascades
        Assert.Empty(read.SoftChildren);
    }

    [Fact]
    public async Task SoftDeletingAParent_KeepsTheWholeTreeBelowAKeptChild()
    {
        using (var db = Db())
        {
            db.Parents.Add(new Parent
            {
                Id = 1,
                Requireds =
                {
                    new RequiredChild
                    {
                        Id = 20,
                        GrandChildren = { new GrandChild { Id = 200 } },
                        SoftGrandChildren = { new SoftGrandChild { Id = 300 } },
                    },
                },
            });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            var parent = db.Parents
                .Include(p => p.Requireds).ThenInclude(c => c.GrandChildren)
                .Include(p => p.Requireds).ThenInclude(c => c.SoftGrandChildren)
                .Single();
            db.Parents.Remove(parent);
            db.ExecutedCommands.Clear();
            await db.SaveChangesAsync();

            Assert.DoesNotContain(db.ExecutedCommands, sql => sql.Contains("DELETE"));
        }

        using var read = Db();
        Assert.Single(read.RequiredChildren);
        Assert.Single(read.GrandChildren);                     // two levels down, still there
        Assert.False(read.SoftGrandChildren.Single().IsDeleted); // its parent was kept, so it isn't deleted either
    }

    [Fact]
    public async Task HardDeletingAPlainParent_StillDeletesItsSoftDeletableChildren()
    {
        using (var db = Db())
        {
            db.Parents.Add(new Parent { Id = 1, Requireds = { new RequiredChild { Id = 20, SoftGrandChildren = { new SoftGrandChild { Id = 300 } } } } });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            db.RequiredChildren.Remove(db.RequiredChildren.Include(c => c.SoftGrandChildren).Single());
            await db.SaveChangesAsync();
        }

        using var read = Db();
        Assert.Empty(read.RequiredChildren);
        Assert.Empty(read.SoftGrandChildren.IgnoreQueryFilters()); // a flag would point at a row that no longer exists
    }

    [Fact]
    public async Task RemovingASoftDeletableRowAndItsOptionalPlainPrincipal_SoftDeletesTheRow()
    {
        using (var db = Db())
        {
            db.SoftLinked.Add(new SoftLinked { Id = 1, Plain = new Plain { Id = 7 } });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            var linked = db.SoftLinked.Include(l => l.Plain).Single();
            db.Remove(linked.Plain!);
            db.Remove(linked);
            await db.SaveChangesAsync();
        }

        using var read = Db();
        Assert.Empty(read.Plains);                                  // removed on purpose: really deleted
        var saved = read.SoftLinked.IgnoreQueryFilters().Single();  // removed on purpose too, but soft-deletable
        Assert.True(saved.IsDeleted);
        Assert.Null(saved.PlainId);                                 // no longer points at the deleted row
    }

    [Fact]
    public async Task RestrictedOptionalLink_BehavesAsEfDoesForTrackedDependents()
    {
        using (var db = Db())
        {
            db.SoftRestricted.Add(new SoftRestricted { Id = 1, Plain = new Plain { Id = 7 } });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            var restricted = db.SoftRestricted.Include(r => r.Plain).Single();
            db.Remove(restricted.Plain!);
            db.Remove(restricted);
            await db.SaveChangesAsync();
        }

        // EF nulls tracked optional keys for Restrict too, so the surviving soft-deleted row just loses the link.
        using var read = Db();
        Assert.Empty(read.Plains);
        var saved = read.SoftRestricted.IgnoreQueryFilters().Single();
        Assert.Equal((true, (int?)null), (saved.IsDeleted, saved.PlainId));
    }

    [Fact]
    public async Task RemovingOnlyAChild_StillDeletesIt()
    {
        using (var db = Db())
        {
            db.Parents.Add(new Parent { Id = 1, Requireds = { new RequiredChild { Id = 20 } } });
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            db.RequiredChildren.Remove(db.RequiredChildren.Single());
            await db.SaveChangesAsync();
        }

        using var read = Db();
        Assert.Empty(read.RequiredChildren);
        Assert.False(read.Parents.Single().IsDeleted);
    }

    [Fact]
    public void ApplySoftDeleteQueryFilters_TwiceAddsTheConditionOnce()
    {
        var builder = new ModelBuilder();
        builder.Entity<Note>().HasQueryFilter(n => n.TenantId == AuditDbContext.CurrentTenant);

        builder.ApplySoftDeleteQueryFilters().ApplySoftDeleteQueryFilters();

        var filter = builder.Model.FindEntityType(typeof(Note))!.GetQueryFilter()!.ToString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(filter, "IsDeleted"));
        Assert.Contains("TenantId", filter);
    }

    [Fact]
    public void ApplySoftDeleteQueryFilters_AfterAnotherFilterReplacedIt_AppliesAgain()
    {
        var builder = new ModelBuilder();
        builder.Entity<Note>();
        builder.ApplySoftDeleteQueryFilters();
        builder.Entity<Note>().HasQueryFilter(n => n.TenantId == AuditDbContext.CurrentTenant);

        builder.ApplySoftDeleteQueryFilters();

        var filter = builder.Model.FindEntityType(typeof(Note))!.GetQueryFilter()!.ToString();
        Assert.Contains("IsDeleted", filter);
        Assert.Contains("TenantId", filter);
    }

    [Fact]
    public void Extensions_RejectNullBuilders()
    {
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).UseAuditing());
        Assert.Throws<ArgumentNullException>(() => ((ModelBuilder)null!).ApplySoftDeleteQueryFilters());
    }

    [Fact]
    public void UseAuditing_AddsInterceptor()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(_connection).UseAuditing().Options;

        var extension = options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>();

        Assert.Single(extension!.Interceptors!.OfType<AuditingInterceptor>());
    }

    [Fact]
    public void QueryFilter_SoftDeletableDerivedTypeOfPlainRoot_Throws()
    {
        var builder = new ModelBuilder();
        builder.Entity<PlainBase>();
        builder.Entity<SoftDerived>().HasBaseType<PlainBase>();

        Assert.Throws<InvalidOperationException>(() => builder.ApplySoftDeleteQueryFilters());
    }

    private class PlainBase
    {
        public int Id { get; set; }
    }

    private sealed class SoftDerived : PlainBase, ISoftDeletable
    {
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
