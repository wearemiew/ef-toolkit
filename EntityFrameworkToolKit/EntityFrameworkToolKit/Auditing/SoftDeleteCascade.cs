using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// Turns deletes of <see cref="ISoftDeletable"/> entities into updates and replays EF's cascade the soft way.
/// </summary>
internal static class SoftDeleteCascade
{
    // Remove(principal) has already cascaded through the tracked dependents: required ones are Deleted and optional
    // ones have a null foreign key. A soft-deleted row stays, so its cascade is replayed the soft way, transitively:
    // dependents of a soft-deleted row are soft-deleted if they can be, and otherwise kept (restored to Unchanged);
    // dependents of a kept row are kept. A plain row that is really deleted still takes its dependents with it, so
    // foreign keys stay consistent. Only cascading foreign keys explain a Deleted row; a row removed on purpose is a
    // root of its own. A surviving row's optional key to a row that is really deleted is nulled by EF's own cascade
    // when the save runs, as for any tracked dependent.
    public static void Apply(ChangeTracker tracker, DateTime now, string? user)
    {
        var deleted = tracker.Entries().Where(e => e.State == EntityState.Deleted && !e.Metadata.IsOwned()).ToList();
        if (deleted.Count == 0)
            return;

        var deletedKeys = new KeyIndex(deleted);
        var survivors = new KeyIndex(); // soft-deleted or kept rows: still in the table

        // Roots: soft-deletable rows removed on purpose, not by another deleted row's cascade.
        foreach (var entry in deleted.Where(e => e.Entity is ISoftDeletable && !deletedKeys.CascadesFrom(e)))
            survivors.Add(SoftDelete(entry, now, user));

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var entry in tracker.Entries().ToList())
            {
                if (entry.State == EntityState.Deleted && !entry.Metadata.IsOwned() && survivors.CascadesFrom(entry))
                {
                    // A soft-deletable child of a kept parent is kept too: its parent is not deleted.
                    survivors.Add(entry.Entity is ISoftDeletable && survivors.CascadesFromSoftDeleted(entry)
                        ? SoftDelete(entry, now, user)
                        : Keep(entry));
                    changed = true;
                }
                else if (entry.State == EntityState.Modified)
                {
                    RestoreNulledForeignKeys(entry, survivors);
                }
            }
        }

    }

    private static EntityEntry SoftDelete(EntityEntry entry, DateTime now, string? user)
    {
        // Unchanged (not Modified), so only the soft-delete columns are written. EF already marked the entity's owned
        // entries as Deleted, which would clear their columns: keep them.
        Keep(entry);

        // Deleting an already-deleted row keeps the original deletion.
        var entity = (ISoftDeletable)entry.Entity;
        if (!entity.IsDeleted)
        {
            AuditRules.Set(entry, nameof(ISoftDeletable.IsDeleted), true);
            AuditRules.Set(entry, nameof(ISoftDeletable.DeletedAt), now);
            if (entity is IUserSoftDeletable)
                AuditRules.Set(entry, nameof(IUserSoftDeletable.DeletedBy), user);
        }

        return entry;
    }

    private static EntityEntry Keep(EntityEntry entry)
    {
        entry.State = EntityState.Unchanged;
        KeepOwned(entry);
        return entry;
    }

    private static void RestoreNulledForeignKeys(EntityEntry entry, KeyIndex survivors)
    {
        foreach (var foreignKey in entry.Metadata.GetForeignKeys().Where(fk => !fk.IsOwnership && survivors.Contains(fk, entry)))
        {
            foreach (var property in foreignKey.Properties.Select(p => entry.Property(p.Name)).Where(p => p.IsModified && p.CurrentValue is null))
            {
                property.CurrentValue = property.OriginalValue;
                property.IsModified = false;
            }
        }
    }

    private static void KeepOwned(EntityEntry owner)
    {
        foreach (var navigation in owner.Navigations)
        {
            if (navigation.Metadata is not INavigation { ForeignKey.IsOwnership: true, IsOnDependent: false } || navigation.CurrentValue is null)
                continue;

            var owned = navigation.Metadata.IsCollection ? ((IEnumerable)navigation.CurrentValue).Cast<object>() : new[] { navigation.CurrentValue };
            foreach (var entity in owned)
            {
                var entry = owner.Context.Entry(entity);
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Unchanged;
                KeepOwned(entry);
            }
        }
    }

    /// <summary>The key values of a set of rows, to find which tracked rows referenced them before any change.</summary>
    private sealed class KeyIndex
    {
        private readonly HashSet<(IReadOnlyKey Key, KeyValues Values)> _keys = new();
        private readonly HashSet<(IReadOnlyKey Key, KeyValues Values)> _softDeletedKeys = new();

        public KeyIndex(IEnumerable<EntityEntry>? entries = null)
        {
            foreach (var entry in entries ?? Enumerable.Empty<EntityEntry>())
                Add(entry);
        }

        public void Add(EntityEntry entry)
        {
            var softDeleted = entry.Entity is ISoftDeletable && entry.State != EntityState.Deleted && ((ISoftDeletable)entry.Entity).IsDeleted;
            foreach (var key in entry.Metadata.GetKeys())
            {
                var item = (key, new KeyValues(key.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray()));
                _keys.Add(item);
                if (softDeleted)
                    _softDeletedKeys.Add(item);
            }
        }

        // Whether the entry was deleted by a cascade from a row in this index: one of its cascading foreign keys pointed
        // (before any change) at it. Non-cascading keys can't explain a delete, so such rows were removed on purpose.
        public bool CascadesFrom(EntityEntry entry) => CascadingForeignKeys(entry).Any(fk => Contains(fk, entry));

        public bool CascadesFromSoftDeleted(EntityEntry entry) =>
            CascadingForeignKeys(entry).Any(fk => _softDeletedKeys.Contains((fk.PrincipalKey, OriginalValues(fk, entry))));

        public bool Contains(IForeignKey foreignKey, EntityEntry entry) => _keys.Contains((foreignKey.PrincipalKey, OriginalValues(foreignKey, entry)));

        private static IEnumerable<IForeignKey> CascadingForeignKeys(EntityEntry entry) =>
            entry.Metadata.GetForeignKeys().Where(fk => !fk.IsOwnership && fk.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade);

        private static KeyValues OriginalValues(IForeignKey foreignKey, EntityEntry entry) =>
            new(foreignKey.Properties.Select(p => entry.Property(p.Name).OriginalValue).ToArray());
    }

    /// <summary>Key values compared by value, including array keys such as <c>byte[]</c>.</summary>
    private readonly struct KeyValues(object?[] values) : IEquatable<KeyValues>
    {
        private readonly object?[] _values = values;

        public bool Equals(KeyValues other) => StructuralComparisons.StructuralEqualityComparer.Equals(_values, other._values);

        public override bool Equals(object? obj) => obj is KeyValues other && Equals(other);

        public override int GetHashCode() => StructuralComparisons.StructuralEqualityComparer.GetHashCode(_values);
    }
}
