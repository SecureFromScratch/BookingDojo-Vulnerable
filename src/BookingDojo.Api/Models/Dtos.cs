namespace BookingDojo.Api.Models;

public record LoginRequest(string Username, string Password);

public record LoginResponse(
    string Token,
    string Username,
    string Role,
    Guid? PartnerId);

public record CreateHotelRequest(
    string Name,
    string Location,
    string Description,
    Guid? PartnerId);

public record HotelDto(
    Guid Id,
    Guid PartnerId,
    string PartnerName,
    string Name,
    string Location,
    string Description,
    bool IsActive,
    DateTime CreatedAt);

public record CreateBookingRequest(
    Guid HotelId,
    DateTime CheckIn,
    DateTime CheckOut,
    string CardLastFour,
    string SpecialRequests);

public record BookingDto(
    int Id,
    Guid UserId,
    string Username,
    Guid HotelId,
    string HotelName,
    DateTime CheckIn,
    DateTime CheckOut,
    string CardLastFour,
    string SpecialRequests,
    DateTime CreatedAt);

public record AuditLogDto(
    Guid Id,
    DateTime Timestamp,
    string Username,
    string Action,
    string Details);

public record RedeemCouponRequest(string Code);

public record ForgotPasswordRequest(string Username);
public record ResetPasswordRequest(string Token, string NewPassword);

public record WebhookTestRequest(string Url);
