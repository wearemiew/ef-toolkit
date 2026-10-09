using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// Stamps <see cref="IAuditable"/>/<see cref="IUserAuditable"/> entities and turns deletes of
/// <see cref="ISoftDeletable"/>/<see cref="IUserSoftDeletable"/> entities into updates on every <c>SaveChanges</c>.
/// Register it with <see cref="AuditingDbContextOptionsExtensions.UseAuditing(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, Func{string?}?, TimeProvider?)"/>.
/// </summary>
/// <remarks>
/// Only <c>SaveChanges</c> is intercepted: <c>ExecuteUpdate</c>, <c>ExecuteDelete</c> and raw SQL are neither audited
/// nor soft-deleted.
/// </remarks>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly Func<string?>? _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditingInterceptor"/> class.
    /// </summary>
    /// <param name="currentUser">
    /// Returns the current user's identifier for the <c>*By</c> fields, called once per save; <see langword="null"/>
    /// leaves them null. Resolve the user inside the delegate (e.g. from <c>IHttpContextAccessor</c>) rather than
    /// capturing a value, so it is right for every save.
    /// </param>
    /// <param name="timeProvider">The clock for the <c>*At</c> fields; defaults to <see cref="TimeProvider.System"/>.</param>
    public AuditingInterceptor(Func<string?>? currentUser = null, TimeProvider? timeProvider = null)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContextEventData eventData)
    {
        if (eventData.Context is { } context)
            AuditRules.Apply(context.ChangeTracker, _timeProvider.GetUtcNow().UtcDateTime, _currentUser?.Invoke());
    }
}
