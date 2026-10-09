using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// Registers <see cref="AuditingInterceptor"/> on a <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class AuditingDbContextOptionsExtensions
{
    /// <summary>
    /// Stamps audit fields and soft-deletes entities on every <c>SaveChanges</c>; see <see cref="AuditingInterceptor"/>.
    /// </summary>
    /// <param name="optionsBuilder">The options being configured.</param>
    /// <param name="currentUser">Returns the current user's identifier for the <c>*By</c> fields; see <see cref="AuditingInterceptor(Func{string?}?, TimeProvider?)"/>.</param>
    /// <param name="timeProvider">The clock for the <c>*At</c> fields; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder UseAuditing(
        this DbContextOptionsBuilder optionsBuilder, Func<string?>? currentUser = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        return optionsBuilder.AddInterceptors(new AuditingInterceptor(currentUser, timeProvider));
    }

    /// <inheritdoc cref="UseAuditing(DbContextOptionsBuilder, Func{string?}?, TimeProvider?)"/>
    public static DbContextOptionsBuilder<TContext> UseAuditing<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder, Func<string?>? currentUser = null, TimeProvider? timeProvider = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)optionsBuilder).UseAuditing(currentUser, timeProvider);
}
