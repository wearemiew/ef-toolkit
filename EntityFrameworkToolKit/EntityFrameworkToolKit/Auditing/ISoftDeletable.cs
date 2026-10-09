namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// An entity that is flagged as deleted instead of being removed: <c>Remove</c> + <c>SaveChanges</c> becomes an
/// <c>UPDATE</c> through <see cref="AuditingInterceptor"/>, and
/// <see cref="SoftDeleteModelBuilderExtensions.ApplySoftDeleteQueryFilters"/> hides flagged rows from queries.
/// </summary>
/// <remarks>
/// <para>
/// To restore an entity, load it with <c>IgnoreQueryFilters()</c>, set <see cref="IsDeleted"/> to false and save:
/// the deletion stamp is cleared for you.
/// </para>
/// <para>
/// A soft delete keeps the entity's dependents: EF's cascade (deleting required dependents, nulling optional foreign
/// keys) is undone for tracked dependents, transitively, and soft-deletable dependents are soft-deleted with it.
/// Untracked dependents are not touched. Changes to the children in the same save look like EF's cascade and are
/// undone too, so save them separately; children added in the same save are dropped by EF before the save starts.
/// </para>
/// <para>
/// Implement the properties implicitly and keep them mapped. <c>Update()</c> or <c>Attach</c> of a detached entity
/// writes every property, including <see cref="IsDeleted"/>: an entity built from a request with
/// <c>IsDeleted = false</c> would un-delete the row. Load the entity and copy the changed values onto it instead.
/// Unique indexes still see deleted rows; give them a filter such as <c>HasFilter("[IsDeleted] = 0")</c>.
/// </para>
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>Whether the entity has been soft-deleted.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the entity was soft-deleted (UTC), or <see langword="null"/> while it is not deleted.</summary>
    DateTime? DeletedAt { get; set; }
}
