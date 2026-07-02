namespace AuthService.Models;

/// <summary>
/// Append-only record of an authentication event (register, login success/failure,
/// refresh, logout, role change). Required by the architecture's auth schema.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>The acting/affected user, when known (null for failed logins of unknown emails).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Event name, e.g. "user.registered", "login.success", "login.failure".</summary>
    public required string Event { get; set; }

    public string? Email { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>Free-form detail (e.g. role transition, failure reason).</summary>
    public string? Detail { get; set; }

    public required string CreatedAt { get; set; }
}
