using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Services;
using BookingDojo.Api.Workshop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "AdminUser,SupportUser")]
public class AuditLogsController : ControllerBase
{
    private readonly BookingDojoDbContext _db;
    private readonly IOptions<WorkshopOptions> _workshopOptions;
    private readonly AuditLogService _auditLogService;

    public AuditLogsController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshopOptions, AuditLogService auditLogService)
    {
        _db = db;
        _workshopOptions = workshopOptions;
        _auditLogService = auditLogService;
    }

    private string CallerUsername => User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "unknown";
    private string CallerRole     => User.FindFirstValue("role") ?? "";

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteLog(Guid id)
    {
        var log = await _db.AuditLogs.FindAsync(id);
        if (log == null) return NotFound();

        if (_workshopOptions.Value.AuditLogDeletion == "Fixed")
        {
            // WORKSHOP: FIXED PATH
            // Only AdminUser may delete — SupportUser is denied (least privilege).
            // The deletion is itself logged so erasure is part of the audit trail.
            if (CallerRole != "AdminUser")
                return Forbid();

            await _auditLogService.LogAsync(
                CallerUsername,
                "LOG_ENTRY_DELETED",
                $"Audit entry {id} deleted (action={log.Action}, user={log.Username}, ts={log.Timestamp:u})",
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        // WORKSHOP: VULNERABLE PATH — no extra role check, no secondary log entry.
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

        if (_workshopOptions.Value.StoredXssAuditLogs == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH
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
        else
        {
            // WORKSHOP: FIXED PATH
            // HTML-encode the Details field before returning it.
            // dangerouslySetInnerHTML will render the encoded text safely.
            var encoder = HtmlEncoder.Default;
            return Ok(logs.Select(l => new AuditLogDto(
                l.Id,
                l.Timestamp,
                l.Username,
                l.Action,
                encoder.Encode(l.Details))));
        }
    }
}
