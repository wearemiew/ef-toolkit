namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// An <see cref="ISoftDeletable"/> entity that also records who deleted it, using the current-user delegate given to
/// <see cref="AuditingInterceptor"/>.
/// </summary>
public interface IUserSoftDeletable : ISoftDeletable
{
    /// <summary>The user who soft-deleted the entity, or <see langword="null"/> while it is not deleted (or there was no user).</summary>
    string? DeletedBy { get; set; }
}
