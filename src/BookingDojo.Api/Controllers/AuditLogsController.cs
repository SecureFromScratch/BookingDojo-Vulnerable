using System.Text.Encodings.Web;
using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
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

    public AuditLogsController(BookingDojoDbContext db, IOptions<WorkshopOptions> workshopOptions)
    {
        _db = db;
        _workshopOptions = workshopOptions;
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
