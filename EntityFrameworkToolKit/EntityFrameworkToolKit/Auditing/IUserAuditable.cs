namespace EntityFrameworkToolKit.Auditing;

/// <summary>
/// An <see cref="IAuditable"/> entity that also records who created and last updated it, using the current-user
/// delegate given to <see cref="AuditingInterceptor"/>.
/// </summary>
public interface IUserAuditable : IAuditable
{
    /// <summary>The user who inserted the entity, or <see langword="null"/> when there was none. Set once.</summary>
    string? CreatedBy { get; set; }

    /// <summary>The user who last inserted, updated or soft-deleted the entity, or <see langword="null"/> when there was none.</summary>
    string? UpdatedBy { get; set; }
}
