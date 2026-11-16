# Exercise 14 — Mass Assignment: Profile Role Escalation (Full Walkthrough)

**Difficulty:** Beginner  
**Category:** Broken Access Control / Mass Assignment (Over-posting)  
**OWASP Top 10:** A01:2021 — Broken Access Control  
**CWE:** CWE-915 — Improperly Controlled Modification of Dynamically-Determined Object Attributes

---

## Scenario

A support user is allowed to update their own display name and bio in BookingDojo. The profile page provides no control for changing roles.

But the backend update endpoint accepts an arbitrary JSON object and passes its fields to a generic model updater. The attacker can therefore change the `Role` property of their own user record to `AdminUser`, log in again, and obtain administrator capabilities.

**Goal:** Reproduce this authorization bypass using browser Developer Tools, explain the source-code defect, replace the unrestricted update with a DTO-based update, and verify that role changes are no longer accepted.

Use only the workshop's `support` account and restore its original role after testing.

---

## Background

**Mass assignment** (also called **over-posting**) occurs when an API maps client-supplied fields directly onto a server-side model without restricting which properties may be modified.

The client may legitimately supply `displayName` and `bio`. But the user model also has fields such as `Role`. If the API trusts arbitrary JSON property names and writes all matching properties, a client can assign a field that the UI never presents.

This is **not** an authentication bypass: the attacker signs in successfully as `support`. It is an **authorization failure**: an authenticated user is allowed to modify a security-sensitive property (`Role`) that should be controlled by the application.

A response that merely echoes a changed role would not by itself prove privilege escalation. This lab confirms that the database update persists and that the next authenticated session receives elevated privileges.

---

## Step 1 — Inspect the normal profile

1. Open BookingDojo using the configured UI address (for example, `http://localhost:5174`).
2. Log in as **`support / Support1234!`**.
3. Open **Developer Tools → Network**, preferably with **Preserve log** enabled.
4. Click **PROFILE** in the top navigation bar.
5. Find the request retrieving the profile, typically via `/bff/profile` (the BFF forwards it to the API).
6. Inspect the response JSON. Note the user name, role, display name, bio, and avatar URL.

The page also displays the account's role in a badge at the top right, **third item from the right**: `SUPPORTUSER`.

**Question:** The API returns a role, but where is the role-editing control in the profile form? There isn't one.

---

## Step 2 — Capture an ordinary profile update

1. Stay on **My Profile**.
2. Change **Display Name** (for example, to `just_me`).
3. Click **Save Profile**.
4. In **Network**, find the update request for the profile.
5. Inspect its HTTP method, path, and **Request Payload**.

The ordinary request is a JSON object containing editable profile fields, for example:

```http
PUT /bff/profile
Content-Type: application/json
```

```json
{
  "displayName": "just_me",
  "bio": ""
}
```

The exact property casing and route should be taken from the captured browser request.

Inspect the response: the newly submitted display name is returned. The request/response cycle proves that the API accepts changes sent by the browser, but not yet that it enforces a list of permitted fields.

---

## Step 3 — Change a hidden field using Developer Tools

1. Right-click the **profile update** request from Step 2.
2. In Chromium-based browsers, choose **Edit and Resend** (often under the request's context menu or Network request details). If your browser uses another label, use its equivalent request-editing/resend feature.
3. Keep the same HTTP method, destination, session credentials, and `Content-Type: application/json`.
4. Replace the request body with:

```json
{
  "role": "AdminUser"
}
```

The value is a reasoned guess from the existing `SupportUser` value. The generic updater matches property names **case-insensitively**, so `role` can resolve to the C# `Role` property.

5. **Send** the modified request.
6. Inspect the response. The returned `ProfileDto` should now show `role: "AdminUser"` (casing depends on JSON serialization).

If your browser doesn't provide Edit and Resend, use **Copy as fetch**, paste it into Developer Tools Console, and change only the JSON request body. Preserve the original URL, credentials, and relevant headers.

**Expected vulnerable behavior:** the server accepts a user-supplied `role` field and updates the current user's stored role.

---

## Step 4 — Verify that this is privilege escalation

1. **Log out** of BookingDojo.
2. **Log in again** with the same `support / Support1234!` credentials.
3. Observe the badge at the upper-right of the application: it should now show `ADMINUSER`.
4. Compare the available top navigation and administrative functionality with the original support view.

Signing out and back in matters: the existing session may contain an older role claim. Reauthentication refreshes the claims from the updated user record.

**Expected:** the support account has gained administrator access. The top navigation now includes administrator-only sections, matching the second supplied screenshot.

Do not use the new privileges to inspect anyone else's information; the changed badge, navigation, and your own account's new role are sufficient for the lab.

---

## Step 5 — Understand the vulnerable code

**File:** `src/BookingDojo.Api/Controllers/ProfileController.cs`

The vulnerable action accepts an unrestricted dictionary:

```csharp
public async Task<IActionResult> UpdateProfile(
    [FromBody] Dictionary<string, System.Text.Json.JsonElement> request)
{
    var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var user = await _db.Users.FindAsync(userId);
    if (user == null) return NotFound();

    await _modelsUpdater.UpdateFromJson(_db.Users, user, request);

    return Ok(new ProfileDto(
        user.Username, user.Role, user.DisplayName, user.Bio, user.AvatarUrl));
}
```

Notice that the controller correctly looks up the **current user**, but it never restricts which properties that user can change.

**File:** `src/BookingDojo.Api/Services/ModelsUpdater.cs`

The generic updater is the root cause:

```csharp
foreach (var (propertyName, jsonValue) in request)
{
    var property = typeof(TEntity).GetProperty(propertyName,
        BindingFlags.Public |
        BindingFlags.Instance |
        BindingFlags.IgnoreCase);

    if (property?.CanWrite != true)
        continue;

    property.SetValue(model,
        JsonSerializer.Deserialize(
            jsonValue.GetRawText(), property.PropertyType));
}

await _db.SaveChangesAsync();
```

**Why this is vulnerable:**

1. `Dictionary<string, JsonElement>` accepts **any JSON property name**, not just `DisplayName` and `Bio`.
2. `GetProperty(... IgnoreCase)` finds corresponding public properties on the entity, even when the request uses a different capitalization.
3. `CanWrite` checks only that a C# setter exists. It is **not an authorization decision**.
4. `JsonSerializer.Deserialize(...)` converts the supplied JSON value to the property's CLR type.
5. `property.SetValue(model, ...)` writes that value to the **tracked Entity Framework entity**.
6. `SaveChangesAsync()` persists the resulting updates.

Because `Role` is a writable property of the user model, an authenticated client can assign it just like `DisplayName`.

The method also creates a broader exposure: when future writable properties are added to the model, they may become remotely modifiable **without any controller change**. The endpoint silently inherits the model's entire writable surface.

---

## Step 6 — Fix with an explicit request DTO

**File:** `src/BookingDojo.Api/Models/` (use the existing location of `UpdateProfileRequest`).

The original application already had the right approach: a dedicated request type containing **only editable profile fields**. Restore it, or define it if missing:

```csharp
public record UpdateProfileRequest(
    string? DisplayName,
    string? Bio);
```

**File:** `src/BookingDojo.Api/Controllers/ProfileController.cs`

Replace the dictionary-based `UpdateProfile` action with the explicit DTO-based version:

```csharp
[HttpPut]
public async Task<IActionResult> UpdateProfile(
    [FromBody] UpdateProfileRequest request)
{
    var userId = Guid.Parse(
        User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    var user = await _db.Users.FindAsync(userId);
    if (user == null) return NotFound();

    user.DisplayName = request.DisplayName;
    user.Bio = request.Bio;

    await _db.SaveChangesAsync();

    return Ok(new ProfileDto(
        user.Username, user.Role, user.DisplayName, user.Bio, user.AvatarUrl));
}
```

The controller now explicitly assigns only `DisplayName` and `Bio`. There is no client-driven path to `user.Role`.

**Important distinction:** Merely deserializing JSON into a DTO is not a complete authorization strategy. ASP.NET Core's default JSON deserialization may ignore unknown JSON properties; this is acceptable here **because the code only copies allowlisted DTO fields** onto the user entity. Even if the attacker includes `"role": "AdminUser"` in the request body, nothing in this controller writes it.

For higher assurance, you may additionally reject unknown request fields and require explicit validation, but the decisive fix here is to stop blindly mapping arbitrary request keys to model properties.

> Keep authorization-sensitive updates, such as role assignment, in a separate, explicitly authorized administrative operation. No ordinary profile update should expose them.

---

## Step 7 — Restore and verify

Before rebuilding the fixed code, **restore the workshop account** if you exploited the vulnerability:

1. While signed in using the elevated support account, repeat **Edit and Resend** for the profile update.
2. Use:

```json
{
  "role": "SupportUser"
}
```

3. Log out, log back in, and confirm the `SUPPORTUSER` badge is restored.

Now rebuild/restart the API with the DTO fix.

Test both behaviors:

**A. Legitimate profile update**

```json
{
  "displayName": "Updated Support",
  "bio": "Profile updated safely"
}
```

Expected: the profile fields update normally.

**B. Attempt the old attack again**

```json
{
  "role": "AdminUser"
}
```

Expected: either a client-error response (if strict unknown-property handling has been configured) or a successful response that **does not change the role** (if unknown fields are ignored). In either case, after logging out and in again, the role must still be `SupportUser`.

Recheck the profile GET response as well as the top-right badge. Do not rely on the UI alone: the server-stored role must remain unchanged.

---

## Step 8 — Related real-world Chainlit vulnerabilities

The broader lesson has appeared in real Chainlit security research. In **CVE-2026-22218**, an authenticated client could supply a custom element containing a user-controlled file `path`, leading to arbitrary file read. In **CVE-2026-22219**, a user-controlled element `url` could trigger server-side requests (SSRF) under certain configurations.

These were **not confirmed examples of profile role mass assignment**, so do not present them as that. They are related illustrations of the dangerous pattern: accepting an over-flexible, attacker-controlled object and passing its fields into a powerful internal operation without sufficient field-specific restrictions.

Sources:
- https://nvd.nist.gov/vuln/detail/CVE-2026-22218
- https://nvd.nist.gov/vuln/detail/CVE-2026-22219
- https://www.zafran.io/resources/chainleak-critical-ai-framework-vulnerabilities-expose-data-enable-cloud-takeover

---

## What to remember

- Editing the request body can bypass every client-side form restriction.
- `CanWrite` means *technically writable*, not *authorized for this caller*.
- Mass assignment can expose both today's sensitive fields and **tomorrow's new fields**.
- Prefer explicit input DTOs and explicit field assignment for updates to security-sensitive entities.
- Verify both persistence and authorization behavior after reauthentication.

**Challenge:** Which other API endpoints in BookingDojo, if any, accept arbitrary property dictionaries or dynamically map them to database entities?
