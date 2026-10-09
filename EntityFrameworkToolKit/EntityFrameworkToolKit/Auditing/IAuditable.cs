namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// An entity whose creation and last-update times are stamped on <c>SaveChanges</c> by <see cref="AuditingInterceptor"/>.
/// </summary>
/// <remarks>
/// Both times are UTC. <see cref="UpdatedAt"/> is also set on insert, so it is never empty and can be used as a sort key
/// (e.g. a "recently updated" cursor feed). Implement the properties implicitly so EF Core maps them as columns.
/// Values set by the caller are always replaced, so imports can't keep historical times through <c>SaveChanges</c>.
/// After <c>Update()</c> of a detached entity, the stored <see cref="CreatedAt"/> is kept, but the entity in memory
/// still holds whatever the caller set, because the stored value isn't known.
/// </remarks>
public interface IAuditable
{
    /// <summary>When the entity was inserted (UTC). Set once; later saves can't change it.</summary>
    DateTime CreatedAt { get; set; }

    /// <summary>When the entity was last inserted, updated or soft-deleted (UTC).</summary>
    DateTime UpdatedAt { get; set; }
}
