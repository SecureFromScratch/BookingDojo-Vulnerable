using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Api.Controllers;

[ApiController]
[Route("api/debug")]
[Authorize]
public class DebugController : ControllerBase
{
    // WORKSHOP: intentionally throws an exception containing sensitive information
    // (fake connection string) to demonstrate exception detail disclosure.
    [HttpGet("throw")]
    public IActionResult TriggerError()
    {
        throw new InvalidOperationException(
            "Read replica connection failed: " +
            "Host=db-replica.bookingdojo.internal;Port=5432;" +
            "Database=bookingdojo_prod;Username=app_svc;Password=Pr0dS3cr3t-2024!" +
            ";SSL Mode=Require;Trust Server Certificate=false");
    }
}
