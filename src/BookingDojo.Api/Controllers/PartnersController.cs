using BookingDojo.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/partners")]
[Authorize(Roles = "AdminUser")]
public class PartnersController : ControllerBase
{
    private readonly BookingDojoDbContext _db;

    public PartnersController(BookingDojoDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetPartners()
    {
        var partners = await _db.Partners
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Ok(partners);
    }
}
