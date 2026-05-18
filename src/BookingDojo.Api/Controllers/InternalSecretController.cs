using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Api.Controllers;

/// <summary>
/// Simulates an internal-only configuration service that relies on network controls
/// rather than authentication. Not exposed through the BFF — only reachable from
/// within the server's own network (or via SSRF).
/// </summary>
[ApiController]
[Route("api/internal")]
public class InternalSecretController : ControllerBase
{
    [HttpGet("secret")]
    public IActionResult GetSecret()
    {
        return Ok(new
        {
            service = "internal-config-v1",
            warning = "INTERNAL USE ONLY — protected by network access controls. Do not expose externally.",
            database = new
            {
                primary = new
                {
                    host = "postgres.bookingdojo.internal",
                    port = 5432,
                    name = "bookingdojo_prod",
                    username = "bookingdojo_app",
                    password = "Pr0dD4t4b4s3S3cr3t-2024!"
                }
            },
            stripe = new
            {
                secretKey = "sk_live_aBcDeFgHiJkLmN0PqRsTuVwXyZ1234567890",
                webhookSecret = "whsec_XyZ987654321abcdefghijklmn0pqrstu"
            },
            internalApiKey = "int-api-k3y-N0tF0rPubl1cUse-2024",
            jwtSigningSecret = "Pr0dJwtS3cr3t!D0-NOT-SHARE-2024"
        });
    }
}
