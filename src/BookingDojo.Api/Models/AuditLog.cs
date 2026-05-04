namespace BookingDojo.Api.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable action name, e.g. "HotelCreated", "UserLogin".
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Free-text detail that may contain user-supplied content.
    /// This field is the attack vector for Lab 01 (Stored XSS).
    /// </summary>
    public string Details { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;
}
