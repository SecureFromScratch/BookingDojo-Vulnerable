namespace BookingDojo.Api.Authorization;

public interface IOwnedResource
{
    Guid UserId { get; }
}
