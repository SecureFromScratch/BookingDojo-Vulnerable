using BookingDojo.Api.Data;
using BookingDojo.Api.Models;

namespace BookingDojo.Api.Services;

public class AuditLogService
{
    private readonly BookingDojoDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(BookingDojoDbContext db, ILogger<AuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string username, string action, string details, string? ipAddress = null)
    {
        var log = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId    = Guid.Empty,
            Username  = username.Length > 100 ? username[..100] : username,
            Action    = action,
            Details   = details,
            IpAddress = ipAddress ?? "unknown"
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();

        // VULNERABLE PATH (log injection)
        // User-controlled values are interpolated directly into the log message.
        // A \n or \r\n inside `username` or `details` creates a new line in the
        // server console that looks like a legitimate log entry to anyone reading
        // a raw log file or terminal dump.
#pragma warning disable CA2254
        _logger.LogInformation($"AUDIT [{action}] user={username} ip={ipAddress ?? "unknown"} :: {details}");
#pragma warning restore CA2254
    }
}
