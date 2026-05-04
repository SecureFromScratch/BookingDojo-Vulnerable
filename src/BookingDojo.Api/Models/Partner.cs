namespace BookingDojo.Api.Models;

public class Partner
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Hotel> Hotels { get; set; } = new List<Hotel>();
}
