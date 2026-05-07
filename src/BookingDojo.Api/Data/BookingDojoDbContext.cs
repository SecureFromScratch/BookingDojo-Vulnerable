using Microsoft.EntityFrameworkCore;
using BookingDojo.Api.Models;

namespace BookingDojo.Api.Data;

public class BookingDojoDbContext : DbContext
{
    public BookingDojoDbContext(DbContextOptions<BookingDojoDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("bookingdojo");

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(100);
            e.Property(u => u.Role).HasMaxLength(50);
            e.HasOne(u => u.Partner)
             .WithMany(p => p.Users)
             .HasForeignKey(u => u.PartnerId)
             .IsRequired(false);
        });

        modelBuilder.Entity<Partner>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Hotel>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Name).HasMaxLength(200);
            e.Property(h => h.Location).HasMaxLength(200);
            e.Property(h => h.PricePerNight).HasColumnType("decimal(10,2)");
            e.HasOne(h => h.Partner)
             .WithMany(p => p.Hotels)
             .HasForeignKey(h => h.PartnerId);
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).ValueGeneratedOnAdd();
            e.Property(b => b.Username).HasMaxLength(100);
            e.Property(b => b.CardLastFour).HasMaxLength(4);
            e.Property(b => b.SpecialRequests).HasMaxLength(500);
            e.Property(b => b.TotalPrice).HasColumnType("decimal(10,2)");
            e.HasOne(b => b.Hotel)
             .WithMany()
             .HasForeignKey(b => b.HotelId);
            e.HasIndex(b => b.UserId);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.Username).HasMaxLength(100);
            e.Property(a => a.IpAddress).HasMaxLength(50);
            e.HasIndex(a => a.Timestamp);
        });

        modelBuilder.Entity<Coupon>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(50);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Token).IsUnique();
            e.Property(t => t.Token).HasMaxLength(100);
            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId);
        });

        modelBuilder.Entity<Cart>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.UserId).IsUnique();
            e.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
            e.HasMany(c => c.Items).WithOne(i => i.Cart).HasForeignKey(i => i.CartId);
            e.Property(c => c.AppliedCouponCode).HasMaxLength(50);
        });

        modelBuilder.Entity<CartItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.CardLastFour).HasMaxLength(4);
            e.Property(i => i.SpecialRequests).HasMaxLength(500);
            e.HasOne(i => i.Hotel).WithMany().HasForeignKey(i => i.HotelId);
        });

        modelBuilder.Entity<MfaChallenge>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Code).HasMaxLength(4);
            e.HasIndex(m => m.UserId);
        });
    }
}
