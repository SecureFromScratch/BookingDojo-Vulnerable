namespace BookingDojo.Api.Models;

public class MfaChallenge
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty; // 4-digit, zero-padded
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}
