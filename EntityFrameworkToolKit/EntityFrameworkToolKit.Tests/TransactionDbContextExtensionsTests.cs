using EntityFrameworkToolKit.Tests.TestHelpers;
using EntityFrameworkToolKit.Transactions;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkToolKit.Tests;

public sealed class TransactionDbContextExtensionsTests : IDisposable
{
    private readonly TestDbContext _db = TestDbContext.CreateSeeded(3);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExecuteInTransactionAsync_SavesAndCommits()
    {
        await _db.ExecuteInTransactionAsync(ct =>
        {
            _db.Entities.Add(new MyEntity { Name = "new" });
            return Task.CompletedTask;
        });

        Assert.Null(_db.Database.CurrentTransaction);
        _db.ChangeTracker.Clear();
        Assert.Equal(4, await _db.Entities.CountAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ReturnsResult()
    {
        var id = await _db.ExecuteInTransactionAsync(async ct =>
        {
            var entity = new MyEntity { Name = "new" };
            _db.Entities.Add(entity);
            await _db.SaveChangesAsync(ct);
            return entity.Id;
        });

        Assert.Equal(4, id);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Exception_RollsBackAndPropagates()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _db.ExecuteInTransactionAsync(async ct =>
        {
            _db.Entities.Add(new MyEntity { Name = "new" });
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal("boom", ex.Message);
        Assert.Null(_db.Database.CurrentTransaction);
        _db.ChangeTracker.Clear();
        Assert.Equal(3, await _db.Entities.CountAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_InsideOuterTransaction_JoinsWithoutCommitting()
    {
        await using (var outer = await _db.Database.BeginTransactionAsync())
        {
            await _db.ExecuteInTransactionAsync(ct =>
            {
                _db.Entities.Add(new MyEntity { Name = "new" });
                return Task.CompletedTask;
            });

            Assert.Same(outer, _db.Database.CurrentTransaction);
            Assert.Equal(4, await _db.Entities.CountAsync());
            await outer.RollbackAsync();
        }

        _db.ChangeTracker.Clear();
        Assert.Equal(3, await _db.Entities.CountAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_TransientFailure_RetriesWholeUnitWithoutDuplicates()
    {
        using var db = TestDbContext.CreateSeeded(3, deps => new RetryOnTransientStrategy(deps));
        var attempts = 0;

        await db.ExecuteInTransactionAsync(async ct =>
        {
            attempts++;
            var first = await db.Entities.SingleAsync(e => e.Id == 1, ct);
            first.Price += 100;
            db.Entities.Add(new MyEntity { Name = "new" });
            await db.SaveChangesAsync(ct);
            if (attempts == 1)
                throw new TransientTestException();
        });

        Assert.Equal(2, attempts);
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.Entities.CountAsync());
        Assert.Equal(101, (await db.Entities.SingleAsync(e => e.Id == 1)).Price);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_FailureAfterCommit_WithoutVerify_RunsTheWorkTwice()
    {
        // The risk verifySucceeded exists for: the commit happened, but the client saw a transient error and retried.
        using var db = TestDbContext.CreateSeeded(3, deps => new RetryOnTransientStrategy(deps), new FailFirstCommit());

        await db.ExecuteInTransactionAsync(_ =>
        {
            db.Entities.Add(new MyEntity { Name = "new" });
            return Task.CompletedTask;
        });

        db.ChangeTracker.Clear();
        Assert.Equal(5, await db.Entities.CountAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_FailureAfterCommit_VerifySucceeded_DoesNotRetry()
    {
        using var db = TestDbContext.CreateSeeded(3, deps => new RetryOnTransientStrategy(deps), new FailFirstCommit());
        var attempts = 0;

        var id = await db.ExecuteInTransactionAsync(
            async ct =>
            {
                attempts++;
                var entity = new MyEntity { Name = "unique" };
                db.Entities.Add(entity);
                await db.SaveChangesAsync(ct);
                return entity.Id;
            },
            verifySucceeded: ct => db.Entities.AsNoTracking().AnyAsync(e => e.Name == "unique", ct));

        Assert.Equal(1, attempts);
        Assert.Equal(4, id);
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.Entities.CountAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_FailureBeforeCommit_VerifyFalse_Retries()
    {
        using var db = TestDbContext.CreateSeeded(3, deps => new RetryOnTransientStrategy(deps));
        var attempts = 0;
        var verified = 0;

        await db.ExecuteInTransactionAsync(
            async ct =>
            {
                attempts++;
                db.Entities.Add(new MyEntity { Name = "unique" });
                await db.SaveChangesAsync(ct);
                if (attempts == 1)
                    throw new TransientTestException();
            },
            verifySucceeded: async ct =>
            {
                verified++;
                return await db.Entities.AsNoTracking().AnyAsync(e => e.Name == "unique", ct);
            });

        Assert.Equal((2, 1), (attempts, verified));
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.Entities.CountAsync());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;

        await _db.ExecuteInTransactionAsync(ct =>
        {
            received = ct;
            return Task.CompletedTask;
        }, cts.Token);

        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _db.ExecuteInTransactionAsync(_ => Task.CompletedTask, cts.Token));
    }

    [Fact]
    public void Arguments_Null_Throw()
    {
        DbContext nullContext = null!;

        Assert.Throws<ArgumentNullException>(() => { _ = nullContext.ExecuteInTransactionAsync(_ => Task.CompletedTask); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.ExecuteInTransactionAsync((Func<CancellationToken, Task>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.ExecuteInTransactionAsync((Func<CancellationToken, Task<int>>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.ExecuteInTransactionAsync(_ => Task.CompletedTask, verifySucceeded: null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = _db.ExecuteInTransactionAsync(_ => Task.FromResult(1), verifySucceeded: null!); });
    }

    private sealed class TransientTestException : Exception;

    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        private bool _failed;

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (_failed)
                return Task.CompletedTask;
            _failed = true;
            throw new TransientTestException();
        }
    }

    private sealed class RetryOnTransientStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;

        protected override TimeSpan? GetNextDelay(Exception lastException) =>
            base.GetNextDelay(lastException) is null ? null : TimeSpan.Zero;
    }
}
