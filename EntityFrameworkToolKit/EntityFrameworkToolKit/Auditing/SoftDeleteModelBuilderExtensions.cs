using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// Hides soft-deleted rows from queries.
/// </summary>
public static class SoftDeleteModelBuilderExtensions
{
    // Remembers the filter this method set, so a second call doesn't add the condition again.
    private const string AppliedFilterAnnotation = "EntityFrameworkToolKit:SoftDeleteQueryFilter";

    /// <summary>
    /// Adds a <c>!IsDeleted</c> query filter to every <see cref="ISoftDeletable"/> entity type, combined (AND) with any
    /// filter the entity type already has. Call it at the end of <c>OnModelCreating</c>: a later <c>HasQueryFilter</c>
    /// replaces the combined filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <c>IgnoreQueryFilters()</c> to see deleted rows (e.g. to restore one). On EF Core 8 it removes every filter
    /// of the query, including tenant filters.
    /// </para>
    /// <para>
    /// The filter also applies when the entity is reached through a navigation: dependents whose required navigation
    /// points at a soft-deleted principal disappear from <c>Include</c> and joins (EF Core warns about a filter on the
    /// required end of a relationship). Make the dependents soft-deletable too, so they are filtered consistently.
    /// </para>
    /// <para>Calling this method more than once is harmless.</para>
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A derived entity type is soft-deletable but its hierarchy's root isn't (query filters can only be set on the root).
    /// </exception>
    public static ModelBuilder ApplySoftDeleteQueryFilters(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
                continue;
            if (entityType.BaseType is { } baseType)
            {
                if (!typeof(ISoftDeletable).IsAssignableFrom(baseType.GetRootType().ClrType))
                    throw new InvalidOperationException(
                        $"{entityType.DisplayName()} is soft-deletable but the root of its hierarchy, " +
                        $"{baseType.GetRootType().DisplayName()}, isn't. Implement ISoftDeletable on the root type.");
                continue;
            }

            if (entityType.FindAnnotation(AppliedFilterAnnotation)?.Value is { } applied && ReferenceEquals(applied, entityType.GetQueryFilter()))
                continue;

            var item = Expression.Parameter(entityType.ClrType, "e");
            Expression filter = Expression.Not(Expression.Property(item, nameof(ISoftDeletable.IsDeleted)));
            if (entityType.GetQueryFilter() is { } existing)
                filter = Expression.AndAlso(ReplacingExpressionVisitor.Replace(existing.Parameters[0], item, existing.Body), filter);
            var combined = Expression.Lambda(filter, item);
            entityType.SetQueryFilter(combined);
            entityType.SetAnnotation(AppliedFilterAnnotation, combined);
        }

        return modelBuilder;
    }
}
