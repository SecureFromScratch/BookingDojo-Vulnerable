using BookingDojo.Api.Data;
using BookingDojo.Api.Models;

namespace BookingDojo.Api.Services;

public class AuditLogService
{
    private readonly BookingDojoDbContext _db;

    public AuditLogService(BookingDojoDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string username, string action, string details, string? ipAddress = null)
    {
        var log = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId = Guid.Empty,
            Username = username,
            Action = action,
            Details = details,
            IpAddress = ipAddress ?? "unknown"
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
