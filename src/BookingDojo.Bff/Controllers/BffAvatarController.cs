using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Bff.Controllers;

// Dedicated handler for avatar file upload — the generic proxy cannot reliably forward
// multipart/form-data because it passes the raw body stream, which ASP.NET Core may have
// already parsed. This controller reads IFormFile directly and rebuilds the multipart
// request for the API, which is the correct pattern for BFF file upload proxying.
[ApiController]
[Route("bff/profile")]
public class BffAvatarController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BffAvatarController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var accessToken = User.FindFirstValue("access_token");
        if (string.IsNullOrEmpty(accessToken))
            return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded" });

        var client = _httpClientFactory.CreateClient("api");

        using var formContent = new MultipartFormDataContent();
        var fileContent = new StreamContent(file.OpenReadStream());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        formContent.Add(fileContent, "file", file.FileName);

        var apiRequest = new HttpRequestMessage(HttpMethod.Post, "/api/profile/avatar")
        {
            Content = formContent
        };
        apiRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(apiRequest);

        Response.StatusCode = (int)response.StatusCode;
        Response.ContentType = "application/json";
        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(body))
            await Response.WriteAsync(body);

        return new EmptyResult();
    }
}
