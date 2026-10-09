using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// Decides the audit and soft-delete fields of every tracked entry before it is saved.
/// </summary>
internal static class AuditRules
{
    /// <summary>
    /// Turns deletes of <see cref="ISoftDeletable"/> entities into updates (undoing the cascades EF applied to their
    /// dependents), clears the deletion stamp of restored entities, then stamps <see cref="IAuditable"/> entities.
    /// Soft delete runs first so that a soft delete also counts as an update.
    /// </summary>
    public static void Apply(ChangeTracker tracker, DateTime now, string? user)
    {
        // The SavingChanges interceptor runs before SaveChanges detects changes, so states may be stale.
        if (tracker.AutoDetectChangesEnabled)
            tracker.DetectChanges();

        SoftDeleteCascade.Apply(tracker, now, user);

        foreach (var entry in tracker.Entries<ISoftDeletable>().Where(e => e.State == EntityState.Modified))
            ClearStampIfRestored(entry);

        foreach (var entry in tracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    Set(entry, nameof(IAuditable.CreatedAt), now);
                    Set(entry, nameof(IAuditable.UpdatedAt), now);
                    if (entry.Entity is IUserAuditable)
                    {
                        Set(entry, nameof(IUserAuditable.CreatedBy), user);
                        Set(entry, nameof(IUserAuditable.UpdatedBy), user);
                    }

                    break;
                case EntityState.Modified:
                    Set(entry, nameof(IAuditable.UpdatedAt), now);
                    Protect(entry, nameof(IAuditable.CreatedAt));
                    if (entry.Entity is IUserAuditable)
                    {
                        Set(entry, nameof(IUserAuditable.UpdatedBy), user);
                        Protect(entry, nameof(IUserAuditable.CreatedBy));
                    }

                    break;
            }
        }
    }

    // A restored entity (IsDeleted set back to false) is no longer deleted, so it has no deletion stamp.
    private static void ClearStampIfRestored(EntityEntry<ISoftDeletable> entry)
    {
        var isDeleted = entry.Property(nameof(ISoftDeletable.IsDeleted));
        if (isDeleted.OriginalValue is not true || entry.Entity.IsDeleted)
            return;

        Set(entry, nameof(ISoftDeletable.DeletedAt), null);
        if (entry.Entity is IUserSoftDeletable)
            Set(entry, nameof(IUserSoftDeletable.DeletedBy), null);
    }

    internal static void Set(EntityEntry entry, string property, object? value) => entry.Property(property).CurrentValue = value;

    // Ignores any change to a set-once field, so an update can't rewrite its creation stamp.
    private static void Protect(EntityEntry entry, string property)
    {
        var field = entry.Property(property);
        field.CurrentValue = field.OriginalValue;
        field.IsModified = false;
    }
}
