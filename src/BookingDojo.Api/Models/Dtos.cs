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
    decimal PricePerNight,
    Guid? PartnerId);

public record HotelDto(
    Guid Id,
    Guid PartnerId,
    string PartnerName,
    string Name,
    string Location,
    string Description,
    decimal PricePerNight,
    bool IsActive,
    DateTime CreatedAt);

public record CreateBookingRequest(
    Guid HotelId,
    DateTime CheckIn,
    DateTime CheckOut,
    string CardNumber,
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
    string? CardNumber,
    string? CardToken,
    string SpecialRequests,
    decimal TotalPrice,
    DateTime CreatedAt);

public record AuditLogDto(
    Guid Id,
    DateTime Timestamp,
    string Username,
    string Action,
    string Details);

public record AddToCartRequest(Guid HotelId, DateTime CheckIn, DateTime CheckOut, string CardNumber, string SpecialRequests);
public record CartCheckoutRequest(string? CouponCode);
public record CartItemDto(int Id, Guid HotelId, string HotelName, DateTime CheckIn, DateTime CheckOut, string CardLastFour, string? CardNumber, string SpecialRequests, decimal TotalPrice);
public record CartDto(int Id, List<CartItemDto> Items, string? AppliedCouponCode, int? AppliedCouponDiscountPercent, int AppliedCouponCount);
public record CheckoutResult(List<BookingDto> Bookings, int? DiscountPercent, string? CouponMessage);

public record RedeemCouponRequest(string Code);

public record MfaVerifyRequest(string Code);

public record ForgotPasswordRequest(string Username);
public record ResetPasswordRequest(string Token, string NewPassword);

public record WebhookTestRequest(string Url);
