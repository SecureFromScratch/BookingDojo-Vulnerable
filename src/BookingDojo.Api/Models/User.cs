namespace BookingDojo.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of: AdminUser, PartnerUser, SupportUser</summary>
    public string Role { get; set; } = string.Empty;

    public Guid? PartnerId { get; set; }
    public Partner? Partner { get; set; }
}
