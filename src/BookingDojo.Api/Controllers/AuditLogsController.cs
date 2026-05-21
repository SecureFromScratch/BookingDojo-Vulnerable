using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "AdminUser,SupportUser")]
public class AuditLogsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly AuditLogService _auditLogService;

    public AuditLogsController(BookingDojoDbContext db, AuditLogService auditLogService)
    {
        _db = db;
        _auditLogService = auditLogService;
    }

    private string CallerUsername => User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";
    private string CallerRole     => User.FindFirstValue("role") ?? "";

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteLog(Guid id)
    {
        var log = await _db.AuditLogs.FindAsync(id);
        if (log == null) return NotFound();

        // VULNERABLE PATH — no extra role check, no secondary log entry.
        // A SupportUser can silently erase any entry, covering their tracks.
        _db.AuditLogs.Remove(log);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> GetAuditLogs()
    {
        var logs = await _db.AuditLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(100)
            .ToListAsync();

        // VULNERABLE PATH
        // The Details field is returned as-is, unsanitized.
        // When the React UI renders it with dangerouslySetInnerHTML,
        // any HTML/JS stored in Details will execute in the browser.
        return Ok(logs.Select(l => new AuditLogDto(
            l.Id,
            l.Timestamp,
            l.Username,
            l.Action,
            l.Details)));
    }
}
