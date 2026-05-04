using BookingDojo.Api.Models;
using BookingDojo.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AuditLogService _auditLogService;

    public AuthController(AuthService authService, AuditLogService auditLogService)
    {
        _authService = authService;
        _auditLogService = auditLogService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (success, token, user) = await _authService.LoginAsync(request.Username, request.Password);

        if (!success || token == null || user == null)
        {
            await _auditLogService.LogAsync(
                request.Username,
                "LOGIN_FAILED",
                $"Failed login attempt for username: {request.Username}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return Unauthorized(new { message = "Invalid username or password" });
        }

        await _auditLogService.LogAsync(
            user.Username,
            "LOGIN_SUCCESS",
            $"User {user.Username} logged in successfully with role {user.Role}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new LoginResponse(token, user.Username, user.Role, user.PartnerId));
    }
}
