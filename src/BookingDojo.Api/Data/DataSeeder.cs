using Microsoft.EntityFrameworkCore;
using BookingDojo.Api.Models;

namespace BookingDojo.Api.Data;

public class DataSeeder
{
    private readonly BookingDojoDbContext _context;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(BookingDojoDbContext context, ILogger<DataSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        // Detect a stale schema (tables missing because a model was added after initial DB creation).
        // Dropping the schema and recreating tables avoids the PostgreSQL "can't drop connected DB" issue.
        var schemaStale = false;
        try
        {
            await _context.Bookings.SumAsync(b => b.TotalPrice);
            await _context.Hotels.SumAsync(h => h.PricePerNight);
            await _context.Coupons.FirstOrDefaultAsync();
            await _context.PasswordResetTokens.FirstOrDefaultAsync();
            await _context.Carts.SumAsync(c => c.AppliedCouponCount);
            await _context.CartItems.FirstOrDefaultAsync();
            await _context.MfaChallenges.FirstOrDefaultAsync();
            await _context.Users.Select(u => u.AvatarUrl).FirstOrDefaultAsync();
            await _context.RefreshTokens.FirstOrDefaultAsync();
            await _context.Webhooks.FirstOrDefaultAsync();
        }
        catch { schemaStale = true; }

        if (schemaStale)
        {
            _logger.LogWarning("[BookingDojo] Schema out of date — dropping schema and recreating.");
            await _context.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS bookingdojo CASCADE");
            await _context.Database.ExecuteSqlRawAsync("CREATE SCHEMA bookingdojo");
            // Schema is now empty; EnsureCreatedAsync sees no tables and creates them all.
            await _context.Database.EnsureCreatedAsync();
        }
        else
        {
            await _context.Database.EnsureCreatedAsync();
            if (await _context.Users.AnyAsync())
            {
                _logger.LogInformation("[BookingDojo] Database already seeded – skipping.");
                return;
            }
        }

        _logger.LogInformation("[BookingDojo] Seeding database...");

        // --- Partners --------------------------------------------------------
        var sunshineId = Guid.NewGuid();
        var mountainId = Guid.NewGuid();
        _context.Partners.AddRange(
            new Partner { Id = sunshineId, Name = "Sunshine Hotels",  IsActive = true },
            new Partner { Id = mountainId, Name = "Mountain Retreats", IsActive = true }
        );

        // --- Users -----------------------------------------------------------
        var adminId   = Guid.NewGuid();
        var partnerId = Guid.NewGuid();
        var supportId = Guid.NewGuid();
        _context.Users.AddRange(
            new User { Id = adminId,   Username = "admin",   PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin1234!"),   Role = "AdminUser" },
            new User { Id = partnerId, Username = "partner", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Partner1234!"), Role = "PartnerUser", PartnerId = sunshineId },
            new User { Id = supportId, Username = "support", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Support1234!"), Role = "SupportUser" }
        );

        // --- Hotels ----------------------------------------------------------
        var grandSunshineId  = Guid.NewGuid();
        var beachParadiseId  = Guid.NewGuid();
        var alpineLodgeId    = Guid.NewGuid();
        _context.Hotels.AddRange(
            new Hotel { Id = grandSunshineId, PartnerId = sunshineId, Name = "Grand Sunshine Hotel",  Location = "Paris, France",      Description = "Luxury hotel in the heart of Paris",    PricePerNight = 250m, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-10) },
            new Hotel { Id = beachParadiseId, PartnerId = sunshineId, Name = "Beach Paradise Resort", Location = "Barcelona, Spain",   Description = "Beachfront resort with stunning views",  PricePerNight = 180m, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-8)  },
            new Hotel { Id = alpineLodgeId,   PartnerId = mountainId, Name = "Alpine Lodge",          Location = "Innsbruck, Austria", Description = "Cosy mountain lodge for winter sports",  PricePerNight = 120m, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-5)  }
        );

        // --- Bookings: IDOR lab (must be first — IDs #1 and #2 matter for the lab) ---
        // admin owns booking #1 (Alpine Lodge, card 1234), partner owns booking #2 (Beach Paradise, card 4242).
        // Different hotels so the SQL injection lab can demonstrate the user-ownership filter clearly:
        // partner searching "Beach" sees their own booking; searching "Alpine" returns nothing until injected.
        _context.Bookings.AddRange(
            new Booking { UserId = adminId,   Username = "admin",   HotelId = alpineLodgeId,   CheckIn = DateTime.UtcNow.AddDays(10), CheckOut = DateTime.UtcNow.AddDays(14), CardLastFour = "1234", CardNumber = "4111111111111234", SpecialRequests = "High floor, away from elevator", TotalPrice = 120m * 4, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new Booking { UserId = partnerId, Username = "partner", HotelId = beachParadiseId,  CheckIn = DateTime.UtcNow.AddDays(20), CheckOut = DateTime.UtcNow.AddDays(23), CardLastFour = "4242", CardNumber = "5500005555554242", SpecialRequests = "Vegan breakfast, late checkout",  TotalPrice = 180m * 3, CreatedAt = DateTime.UtcNow.AddDays(-1) }
        );

        // --- Bookings: resource consumption lab (210 bookings for partner) ----
        // Demonstrates unbounded search results before the fix is applied.
        // Spread across all three hotels with varying dates and card numbers.
        var hotelPool = new[] { grandSunshineId, beachParadiseId, alpineLodgeId };
        for (var i = 1; i <= 210; i++)
        {
            _context.Bookings.Add(new Booking
            {
                UserId          = partnerId,
                Username        = "partner",
                HotelId         = hotelPool[(i - 1) % hotelPool.Length],
                CheckIn         = DateTime.UtcNow.AddDays(i % 90 + 30),
                CheckOut        = DateTime.UtcNow.AddDays(i % 90 + 33),
                CardLastFour    = (i % 10000).ToString("D4"),
                SpecialRequests = $"Booking {i} of 210",
                CreatedAt       = DateTime.UtcNow.AddDays(-i - 2),
            });
        }

        // --- Coupons: race condition lab -------------------------------------
        _context.Coupons.AddRange(
            new Coupon { Code = "SAVE10",   DiscountPercent = 10, MaxUses = 1, UsesCount = 0 },
            new Coupon { Code = "SUMMER20", DiscountPercent = 20, MaxUses = 3, UsesCount = 0 }
        );

        // --- Audit logs (initial history) ------------------------------------
        _context.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), UserId = adminId,   Username = "admin",   Action = "UserLogin",    Details = "User 'admin' logged in",                 Timestamp = DateTime.UtcNow.AddDays(-5), IpAddress = "127.0.0.1" },
            new AuditLog { Id = Guid.NewGuid(), UserId = partnerId, Username = "partner", Action = "HotelCreated", Details = "Created hotel: Grand Sunshine Hotel",    Timestamp = DateTime.UtcNow.AddDays(-4), IpAddress = "127.0.0.1" },
            new AuditLog { Id = Guid.NewGuid(), UserId = partnerId, Username = "partner", Action = "HotelCreated", Details = "Created hotel: Beach Paradise Resort",   Timestamp = DateTime.UtcNow.AddDays(-3), IpAddress = "127.0.0.1" },
            new AuditLog { Id = Guid.NewGuid(), UserId = adminId,   Username = "admin",   Action = "UserLogin",    Details = "User 'admin' logged in",                 Timestamp = DateTime.UtcNow.AddDays(-1), IpAddress = "127.0.0.1" }
        );

        await _context.SaveChangesAsync();
        _logger.LogInformation("[BookingDojo] Seeding complete. Partners: 2, Users: 3, Hotels: 3, Bookings: 212, Coupons: 2, AuditLogs: 4.");
    }
}
