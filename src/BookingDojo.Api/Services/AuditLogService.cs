using BookingDojo.Api.Data;
using BookingDojo.Api.Models;
using BookingDojo.Api.Workshop;
using Microsoft.Extensions.Options;

namespace BookingDojo.Api.Services;

public class AuditLogService
{
    private readonly BookingDojoDbContext _db;
    private readonly ILogger<AuditLogService> _logger;
    private readonly IOptions<WorkshopOptions> _workshop;

    public AuditLogService(BookingDojoDbContext db, ILogger<AuditLogService> logger, IOptions<WorkshopOptions> workshop)
    {
        _db = db;
        _logger = logger;
        _workshop = workshop;
    }

    public async Task LogAsync(string username, string action, string details, string? ipAddress = null)
    {
        var log = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId    = Guid.Empty,
            Username  = username,
            Action    = action,
            Details   = details,
            IpAddress = ipAddress ?? "unknown"
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();

        if (_workshop.Value.LogInjection == "Vulnerable")
        {
            // WORKSHOP: VULNERABLE PATH (log injection)
            // User-controlled values are interpolated directly into the log message.
            // A \n or \r\n inside `username` or `details` creates a new line in the
            // server console that looks like a legitimate log entry to anyone reading
            // a raw log file or terminal dump.
#pragma warning disable CA2254  // template should be a constant — intentionally violated here
            _logger.LogInformation($"AUDIT [{action}] user={username} ip={ipAddress ?? "unknown"} :: {details}");
#pragma warning restore CA2254
        }
        else
        {
            // WORKSHOP: FIXED PATH
            // Control characters are stripped so injected newlines can't forge new log lines.
            // Structured logging (named placeholders) keeps each value as a distinct field —
            // the log provider never embeds it raw into the message string.
            _logger.LogInformation(
                "AUDIT {Action} user={Username} ip={IpAddress} :: {Details}",
                action, Sanitize(username), ipAddress ?? "unknown", Sanitize(details));
        }
    }

    private static string Sanitize(string value) =>
        value
            .Replace("\r\n", "\\r\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
}
