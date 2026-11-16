# Exercise 13 — JavaScript Execution via SVG Avatar Upload (Full Walkthrough)

**Difficulty:** Beginner / Intermediate  
**Category:** Unrestricted File Upload / Active Content / Cross-Site Scripting  
**OWASP Top 10:** A05:2021 — Security Misconfiguration / A03:2021 — Injection

---

## Scenario

BookingDojo allows users to upload a profile picture. The form accepts images, including SVG files. That seems reasonable: SVG is an image format. But SVG is also an XML document that can contain JavaScript.

The application stores the uploaded file inside a Base64 **data URL**, and the profile page displays that URL as an avatar. Your task is to find out whether an attacker-controlled SVG stays harmless in every browser context, demonstrate a safe script-execution proof of concept, and change the server so the uploaded image cannot execute JavaScript when viewed as a document.

Use the harmless test file **`labs/sfs-avatar-script-test.svg`** supplied with the workshop. Only test against your own BookingDojo lab instance.

---

## Background

- An SVG inside `<img src="...">` is rendered in **image mode**: embedded scripts are disabled.
- An SVG loaded as a **document** (for example, in a standalone SVG browsing context) can execute scripts unless the browser or response security policy prevents it.
- Base64 is an *encoding*, not sanitization. `data:image/svg+xml;base64,...` retains the SVG's content and capabilities.
- Modern browsers generally assign `data:` documents an opaque (`null`) origin. That stops same-origin reads of BookingDojo pages and cookies, **but does not automatically prevent JavaScript from running**. Some cross-origin fetches are permitted when the remote server allows them through CORS.
- Modern browsers may block direct top-level navigation to `data:` URLs. That browser restriction is separate from whether a data-backed SVG can execute when loaded in a document context.

The bug is **not** that the avatar automatically executes scripts on the Profile page. The dangerous transition is **the same uploaded SVG being interpreted as an active document** rather than as an image.

---

## Step 1 — Find the upload feature

1. Open BookingDojo (typically `http://localhost:5173`; use your actual Vite port if different).
2. Log in with a workshop account, such as `partner / Partner1234!`.
3. Click **PROFILE** in the top navigation bar.
4. On **My Profile**, find **Profile Picture** beneath **Edit Profile**.
5. Select **Upload File**, **not** **Fetch from URL**.
6. Click **Choose File**, select `labs/sfs-avatar-script-test.svg`, and submit the upload with the page's upload control.
7. Look at the small circular avatar beside your username at the top of the page.

**Expected:** the SVG appears as the avatar. Its text/initial appearance may say it is loading or waiting, but the SVG's JavaScript should **not** run inside the page's `<img>`.

> The separate **Fetch from URL** option is a different attack surface (SSRF) and is not used in this exercise.

---

## Step 2 — Inspect what was stored

Open **DevTools → Network**, then upload again if necessary. Find the avatar upload request; inspect its JSON response.

The vulnerable API action is `POST /api/profile/avatar` (typically reached through the BFF). The response contains an `avatarUrl` similar to:

```text
data:image/svg+xml;base64,PHN2ZyB4bWxucz0i...
```

The value is not a location of a separately hosted file. **It contains the entire uploaded SVG.** It is also stored in the user's `AvatarUrl` database property and later returned by the profile API.

In **DevTools → Elements**, locate the avatar `<img>` and inspect its `src`. Confirm it is the same `data:image/svg+xml;base64,...` value.

Check the following:

1. What MIME type is declared by the data URL?
2. Is the SVG stored as bytes on a server, or embedded in the URL itself?
3. Does seeing the image through `<img>` prove that its JavaScript cannot run in another context?

---

## Step 3 — Demonstrate the rendering-context difference

**First, test image mode.** The avatar inside the Profile page should show the static SVG image without executing its embedded script. This is expected browser behavior and **not** proof that the upload is safe.

**Next, test document mode.** To avoid relying on direct address-bar navigation to a `data:` URL (which some browsers block), use the browser console on your **own lab page** to create a standalone browsing context inside an iframe:

```javascript
// Run only on your local BookingDojo lab page.
const avatar = document.querySelector('img[src^="data:image/svg+xml;base64,"]');
if (!avatar) throw new Error('SVG avatar <img> not found; inspect its selector');

const frame = document.createElement('iframe');
frame.title = 'SVG document-context lab test';
frame.style.cssText = 'width:650px;height:330px;border:2px solid #777';
frame.src = avatar.src;
document.body.append(frame);
```

The `<iframe>` creates a **document context**, unlike `<img>`. Watch the SVG and **DevTools → Console/Network** for the safe behavior encoded in the supplied test file. The exact visible result depends on which test SVG version is in your labs folder and on browser/CSP policy.

If the iframe is blocked, inspect the browser error: a CSP restriction or browser-specific `data:` navigation policy is not the same as SVG being a passive image format. The important question is whether *active SVG document execution* has been prevented by an explicit policy.

> **Origin check:** The SVG document ordinarily has a `null` origin, not BookingDojo's origin. `document.cookie` may throw a `SecurityError` or be unavailable; this is expected. Successful execution does **not** imply cookie theft or same-origin access to BookingDojo.

> **If the supplied SVG fetches jQuery:** A CORS-enabled CDN can allow the request from `Origin: null`; execution through `eval()` additionally depends on the active document's CSP. The lab's visible indicator is a demonstration of script execution, not an attempt to access user data.

---

## Step 4 — Read the vulnerable server code

**File:** `src/BookingDojo.Api/Controllers/ProfileController.cs`

The relevant part of `UploadAvatar` in the vulnerable version is:

```csharp
[HttpPost("avatar")]
public async Task<IActionResult> UploadAvatar(IFormFile file)
{
    if (file.Length > 512 * 1024)
        return BadRequest(new { message = "File too large (max 512 KB)" });

    if (!file.ContentType.StartsWith("image/"))
        return BadRequest(new { message = "Only image files are accepted" });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var base64 = Convert.ToBase64String(ms.ToArray());
    var avatarUrl = $"data:{file.ContentType};base64,{base64}";

    // ... find user ...
    user.AvatarUrl = avatarUrl;
    await _db.SaveChangesAsync();
    return Ok(new { message = "Avatar updated", avatarUrl = avatarUrl });
}
```

Two facts matter:

1. `file.ContentType` comes from the client. The `StartsWith("image/")` test is **not** file-content validation.
2. Even a perfectly valid SVG is potentially **active content**. The server blindly turns it into a `data:` URL, without any HTTP response on which it could impose `Content-Security-Policy` headers.

This is why treating every `image/*` upload as a passive image is an unsafe assumption. In contrast, passive browser image formats such as PNG, JPEG, GIF, and WebP do not intentionally execute embedded JavaScript.

---

## Step 5 — Apply the workshop fix

We want to preserve the existing data model: **the database stores only a string URL in `AvatarUrl`**, not a separate image blob or uploaded file. Therefore this lab uses a deliberately limited workaround:

- **Passive images:** continue using `data:<mime>;base64,<bytes>` URLs.
- **SVG:** store a **relative endpoint URL** containing the SVG's Base64 bytes as a query parameter.
- **When that endpoint is requested:** decode the SVG and serve it as `image/svg+xml` with a **restrictive response CSP**, so opening it as a standalone document does not run scripts.

### 5.1 — Route SVG uploads to a protected endpoint

In `ProfileController.UploadAvatar`, replace the current `avatarUrl` construction with:

```csharp
var base64 = Convert.ToBase64String(ms.ToArray());
//var avatarUrl = $"data:{file.ContentType};base64,{base64}";

var isSvg = file.ContentType.Split(';')[0].Trim()
        .Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);

var avatarUrl = isSvg
        ? "/bff/profile/unembedImage?base64=" + Uri.EscapeDataString(base64)
        : $"data:{file.ContentType};base64,{base64}";
```

**Why this BFF-facing URL?** The confirmed browser-visible route is `/bff/profile/unembedImage?base64=...` (for example, `http://localhost:5174/bff/profile/unembedImage?base64=...`). `Url.Action(..., Request.Scheme)` would generate an absolute URL for the API host and bypass the BFF. Simply omitting `Request.Scheme` generates `/api/profile/unembedImage?...`, which is **not** the confirmed browser-facing route. The example therefore uses the mapped BFF path explicitly. `Uri.EscapeDataString` protects Base64 characters in the query string.

### 5.2 — Add the safe SVG-serving endpoint

Add this action inside `ProfileController`:

```csharp
[HttpGet("unembedImage")]
public IActionResult UnembedImage([FromQuery] string base64)
{
    byte[] bytes;
    try
    {
        bytes = Convert.FromBase64String(base64);
    }
    catch (FormatException)
    {
        return BadRequest("Invalid Base64");
    }

    if (bytes.Length > 512 * 1024)
        return BadRequest("Image too large");

    Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; script-src 'none'; sandbox";
    Response.Headers["X-Content-Type-Options"] = "nosniff";
    Response.Headers["Cache-Control"] = "no-store";

    return File(bytes, "image/svg+xml");
}
```

There is intentionally **no** `[AllowAnonymous]` attribute: the controller already has `[Authorize]`. Keep it authenticated if the avatar is fetched through the BFF, where the session cookie can be translated into an API bearer token. An HTML `<img>` request sends eligible cookies automatically, but **does not** add an `Authorization: Bearer` header itself.

**Deployment check:** The browser uses `/bff/profile/unembedImage`, and the BFF forwards that request to the API's `/api/profile/unembedImage` with the user's authorization. The frontend must proxy `/bff` to the BFF (or the browser must address the BFF directly).

> **Critical: The BFF must propagate the API's CSP response header.** Setting `Content-Security-Policy` in `ProfileController.UnembedImage` is insufficient if `BffProxyController` builds a new response and does not copy that header. The final **browser-facing response** from `/bff/profile/unembedImage` must include `Content-Security-Policy: default-src 'none'; script-src 'none'; sandbox` and `X-Content-Type-Options: nosniff`. Otherwise, the SVG may execute JavaScript again when opened as a document. Update the BFF's response-header forwarding or set the policy explicitly on this BFF route. Do not assume that forwarding the body and MIME type also forwards security headers.

**Security check:** Do not let callers choose the returned Content-Type. This endpoint always returns `image/svg+xml` and CSP. It is meant only for SVGs. An exact MIME allowlist on the upload side (`image/png`, `image/jpeg`, `image/gif`, `image/webp`, `image/svg+xml`) is advisable rather than accepting arbitrary `image/*` subtypes. For a hardened implementation, verify/sanitize uploaded SVG content as defense in depth.

### 5.3 — Keep existing data URLs in perspective

The upload change affects **new** avatar uploads. Any already-stored SVG data URLs remain in the database until replaced or migrated. Upload the sample SVG again after restarting the API to replace your old avatar URL.

---

## Step 6 — Verify the fix

1. Restart the API (and BFF if its routes/configuration changed).
2. Return to **PROFILE → Profile Picture → Upload File**.
3. Re-upload `labs/sfs-avatar-script-test.svg`.
4. Check the Network response and the avatar `<img src>`.

**Expected new value:**

```text
/bff/profile/unembedImage?base64=PHN2Zy...
```

(This is the confirmed browser-facing route; the frontend shown in the lab runs on `localhost:5174`.)

5. Open the image endpoint **as a document** using the authenticated BFF-facing URL; alternatively, point a test iframe at the endpoint URL.
6. Inspect the **browser-facing `/bff/profile/unembedImage` GET response**, not just the backend API response, in **DevTools → Network → Headers**. Confirm the BFF preserved the CSP and `nosniff` headers.

**Expected response headers:**

```http
Content-Type: image/svg+xml
Content-Security-Policy: default-src 'none'; script-src 'none'; sandbox
X-Content-Type-Options: nosniff
```

**Expected behavior:** the SVG renders, but its embedded JavaScript does **not** execute. The jQuery fetch/animation indicator (if present in the supplied test SVG) does not run. The console may show CSP violation messages. Check the Profile page still displays the avatar. Also upload a normal PNG or JPEG and confirm it still uses a `data:image/...;base64,...` URL.

**Negative test:** Loading the SVG endpoint in an image context must remain safe; opening it as a document must now also remain script-inert.

---

## Step 7 — Why this is a workaround, not the final architecture

The database currently has one `AvatarUrl` string field. The workaround keeps that design **without adding server-side image storage** by placing the **entire SVG** into the `base64=` GET query parameter.

That has a serious limitation: **URL length**.

- Base64 increases binary content length by roughly **33%**.
- Query-string URL escaping can increase it further.
- Browsers, Kestrel, the BFF, reverse proxies, CDNs, and hosting platforms impose request-line/query limits. An oversized URL can fail with `414 URI Too Long` or `400 Bad Request`, often **before the controller is reached**.
- A 512 KB SVG may produce a URL of roughly 700 KB or more, which is far beyond common practical URL limits.
- Embedded content in GET URLs may also appear in request logs, browser history, telemetry, or reverse-proxy logs.

### Better production design

Store images in **blob storage** (e.g., Google Cloud Storage, S3, Azure Blob Storage) or **server-side file storage**, with a generated identifier or short URL in the database. This can be done only for SVG initially, or, better, consistently for **all images**.

Serve SVG from a controlled endpoint with an appropriate `Content-Type`, `X-Content-Type-Options: nosniff`, and restrictive CSP; keep its authorization and caching rules explicit. For stronger isolation, use a dedicated cookieless image origin and sanitize or rasterize uploads if SVG features are not required.

This changes the stored reference from a huge embedded payload to a stable, short identifier while preserving the same security policy.

---

## How it works at runtime

```text
User uploads SVG via Profile → Upload File
         │
         ▼
ProfileController.UploadAvatar()
         │
         ├─ Vulnerable:
         │    AvatarUrl = data:image/svg+xml;base64,...
         │         │
         │         ├─ Viewed as <img> → scripts disabled
         │         └─ Loaded as document → scripts may execute
         │
         └─ Workshop fix:
              AvatarUrl = /bff/profile/unembedImage?base64=...
                         │
                         ▼
              BFF authenticates/proxies request
                         │
                         ▼
              UnembedImage returns image/svg+xml
              + CSP: default-src 'none'; script-src 'none'; sandbox
                         │
                         ├─ Viewed as <img> → scripts disabled
                         └─ Loaded as document → CSP blocks scripts
```

---

## Key Takeaways

| | Vulnerable | Workshop fix | Preferred design |
|---|---|---|---|
| SVG storage reference | Base64 `data:` URL | Endpoint URL with Base64 query | Short image ID/URL |
| Standalone SVG scripts | Can execute | Blocked by response CSP | Blocked by CSP; sanitize/rasterize as appropriate |
| Avatar `<img>` | Script-inert | Script-inert | Script-inert |
| URL length | Already large | **Can exceed GET limits** | Small, stable URL |
| File storage | None | None | Blob/file storage |

**The root problem:** treating an active SVG document like an ordinary passive image. **The workshop fix:** serve SVG through an HTTP endpoint that can impose CSP while preserving the URL-only DB field. **The long-term fix:** store images separately and keep only a short reference in the DB.

---

## Further Reading

- [MDN — SVG as an image](https://developer.mozilla.org/en-US/docs/Web/SVG/Guides/SVG_as_an_image)
- [MDN — Content Security Policy](https://developer.mozilla.org/en-US/docs/Web/HTTP/CSP)
- [MDN — Data URLs](https://developer.mozilla.org/en-US/docs/Web/URI/Reference/Schemes/data)
- [OWASP — File Upload Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)
