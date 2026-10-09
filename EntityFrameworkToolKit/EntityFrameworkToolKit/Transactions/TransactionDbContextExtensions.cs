using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkToolKit.Transactions;

/// <summary>
/// Runs a unit of work in a database transaction, the way EF Core expects with retrying execution strategies.
/// </summary>
public static class TransactionDbContextExtensions
{
    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction: begins it, runs the operation, saves pending changes and
    /// commits. Any exception rolls the transaction back and propagates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attempt runs through the context's execution strategy, so with a retrying strategy (e.g.
    /// <c>EnableRetryOnFailure</c>) a transient failure retries the whole unit. Before a retry the change tracker is
    /// cleared, so the operation must do all its work, including loading entities, inside the delegate.
    /// </para>
    /// <para>
    /// A connection failure while committing leaves it unknown whether the commit happened, and a plain retry would
    /// apply the work twice. When that matters (e.g. inserts without a natural key), use the overload taking
    /// <c>verifySucceeded</c>: after a retriable failure it is asked first, and returning <see langword="true"/> ends
    /// without retrying.
    /// </para>
    /// <para>
    /// When a transaction is already open on the context, the operation joins it: its changes are saved but not
    /// committed, and the outer caller decides (and retries); <c>verifySucceeded</c> is then not used.
    /// </para>
    /// </remarks>
    /// <param name="context">The context.</param>
    /// <param name="operation">The work to do; it receives the cancellation token.</param>
    /// <param name="cancellationToken">A token to cancel the database calls.</param>
    /// <returns>A task that completes when the transaction is committed.</returns>
    public static Task ExecuteInTransactionAsync(
        this DbContext context, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(context, WithoutResult(operation), verifySucceeded: null, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction, like
    /// <see cref="ExecuteInTransactionAsync(DbContext, Func{CancellationToken, Task}, CancellationToken)"/>, and asks
    /// <paramref name="verifySucceeded"/> whether a failed attempt was in fact committed before retrying it.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ExecuteInTransactionAsync(DbContext, Func{CancellationToken, Task}, CancellationToken)" path="/remarks"/>
    /// </remarks>
    /// <param name="context">The context.</param>
    /// <param name="operation">The work to do; it receives the cancellation token.</param>
    /// <param name="verifySucceeded">
    /// Checks, in a new query, whether the failed attempt was committed (e.g. whether the inserted row exists).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the database calls.</param>
    /// <returns>A task that completes when the transaction is committed.</returns>
    public static Task ExecuteInTransactionAsync(
        this DbContext context,
        Func<CancellationToken, Task> operation,
        Func<CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(verifySucceeded);
        return ExecuteAsync(context, WithoutResult(operation), verifySucceeded, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction and returns its result: begins the transaction, runs the
    /// operation, saves pending changes and commits. Any exception rolls the transaction back and propagates.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ExecuteInTransactionAsync(DbContext, Func{CancellationToken, Task}, CancellationToken)" path="/remarks"/>
    /// </remarks>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="operation">The work to do; it receives the cancellation token.</param>
    /// <param name="cancellationToken">A token to cancel the database calls.</param>
    /// <returns>The operation's result, once the transaction is committed.</returns>
    public static Task<TResult> ExecuteInTransactionAsync<TResult>(
        this DbContext context, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(context, operation, verifySucceeded: null, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction and returns its result, like
    /// <see cref="ExecuteInTransactionAsync{TResult}(DbContext, Func{CancellationToken, Task{TResult}}, CancellationToken)"/>,
    /// and asks <paramref name="verifySucceeded"/> whether a failed attempt was in fact committed before retrying it.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ExecuteInTransactionAsync(DbContext, Func{CancellationToken, Task}, CancellationToken)" path="/remarks"/>
    /// </remarks>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="context">The context.</param>
    /// <param name="operation">The work to do; it receives the cancellation token.</param>
    /// <param name="verifySucceeded">
    /// Checks, in a new query, whether the failed attempt was committed. When it was, the result that attempt's
    /// operation returned is returned.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the database calls.</param>
    /// <returns>The operation's result, once the transaction is committed.</returns>
    public static Task<TResult> ExecuteInTransactionAsync<TResult>(
        this DbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        Func<CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(verifySucceeded);
        return ExecuteAsync(context, operation, verifySucceeded, cancellationToken);
    }

    private static Func<CancellationToken, Task<bool>> WithoutResult(Func<CancellationToken, Task> operation) =>
        async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return true;
        };

    private static async Task<TResult> ExecuteAsync<TResult>(
        DbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        Func<CancellationToken, Task<bool>>? verifySucceeded,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            var joined = await operation(cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return joined;
        }

        var attempt = 0;
        var lastResult = default(TResult)!; // the result of the latest attempt's operation, for verifySucceeded
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(
            operation,
            async (_, op, ct) =>
            {
                // A failed attempt leaves its entities tracked; re-running the operation would add them again.
                if (attempt++ > 0)
                    context.ChangeTracker.Clear();
                lastResult = default!;

                await using var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                lastResult = await op(ct).ConfigureAwait(false);
                await context.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return lastResult;
            },
            verifySucceeded is null
                ? null
                : async (_, _, ct) => new ExecutionResult<TResult>(await verifySucceeded(ct).ConfigureAwait(false), lastResult),
            cancellationToken).ConfigureAwait(false);
    }
}
