using Microsoft.AspNetCore.Mvc;

namespace BookingDojo.Bff.Controllers;

[ApiController]
[Route("bff/{**path}")]
public class BffProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string CookieName = "bd_token";

    public BffProxyController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpPatch]
    [HttpDelete]
    public async Task<IActionResult> Proxy(string path)
    {
        var token = Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var client = _httpClientFactory.CreateClient("api");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var targetUrl = $"/api/{path}";
        if (Request.QueryString.HasValue)
            targetUrl += Request.QueryString.Value;

        HttpResponseMessage response;
        if (Request.Method == HttpMethods.Post || Request.Method == HttpMethods.Put || Request.Method == HttpMethods.Patch)
        {
            var requestContent = new StreamContent(Request.Body);
            requestContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(Request.Method), targetUrl)
            {
                Content = requestContent
            });
        }
        else
        {
            response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(Request.Method), targetUrl));
        }

        Response.StatusCode = (int)response.StatusCode;
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
        Response.ContentType = contentType;

        var responseBody = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(responseBody))
            await Response.WriteAsync(responseBody);

        return new EmptyResult();
    }
}
