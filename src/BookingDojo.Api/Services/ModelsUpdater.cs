using System.Reflection;
using System.Text.Json;
using BookingDojo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Services;

public class ModelsUpdater
{
    private readonly BookingDojoDbContext _db;

    public ModelsUpdater(BookingDojoDbContext db)
    {
        _db = db;
    }

    public async Task UpdateFromJson<TEntity>(
        DbSet<TEntity> table,
        TEntity model,
        Dictionary<string, JsonElement> request)
        where TEntity : class
    {
        if (_db.Entry(model).State == EntityState.Detached)
            table.Attach(model);

        // VULNERABLE PATH: dynamically assign every supplied writable model property.
        // Request fields are not allowlisted, so callers can over-post sensitive fields.
        foreach (var (propertyName, jsonValue) in request)
        {
            var property = typeof(TEntity).GetProperty(propertyName,
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.IgnoreCase);

            if (property?.CanWrite != true)
                continue;

            property.SetValue(model,
                JsonSerializer.Deserialize(jsonValue.GetRawText(), property.PropertyType));
        }

        await _db.SaveChangesAsync();
    }
}
