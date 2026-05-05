namespace BookingDojo.Api.Workshop;

/// <summary>
/// Controls which implementation (Vulnerable or Fixed) is active for each workshop exercise.
/// Configure in appsettings.json under "BookingDojo:Workshop".
/// </summary>
public class WorkshopOptions
{
    public const string Section = "BookingDojo:Workshop";

    /// <summary>
    /// Lab 01 – Stored XSS in Audit Logs.
    /// "Vulnerable" – Returns raw, unsanitized HTML in AuditLog.Details.
    /// "Fixed"      – HTML-encodes Details before returning (prevents XSS).
    /// </summary>
    public string StoredXssAuditLogs { get; set; } = "Fixed";

    /// <summary>
    /// Lab 02 – IDOR on Bookings.
    /// "Vulnerable" – GET /api/bookings/{id} returns any booking for any authenticated user.
    /// "Fixed"      – Returns 403 if the booking does not belong to the caller.
    /// </summary>
    public string BookingIdorAccess { get; set; } = "Fixed";

    /// <summary>
    /// Lab 03 – SQL Injection in Booking Search.
    /// "Vulnerable" – GET /api/bookings/search?q= concatenates the query string directly into raw SQL.
    /// "Fixed"      – Uses EF Core LINQ (parameterized queries), injection is not possible.
    /// </summary>
    public string BookingSearchSqlInjection { get; set; } = "Fixed";

    /// <summary>
    /// Lab 04 – Time-Based Blind SQL Injection in Login.
    /// "Vulnerable" – POST /api/auth/login concatenates the username into raw SQL.
    ///                The endpoint returns only 200/401 — no data is echoed back.
    ///                Timing reveals whether a condition against internal tables is true.
    /// "Fixed"      – Uses EF Core LINQ (parameterized), no SQL injection possible.
    /// </summary>
    public string LoginSqlInjection { get; set; } = "Fixed";

    /// <summary>
    /// Lab 05 – Uncontrolled Resource Consumption in Booking Search.
    /// "Vulnerable" – GET /api/bookings/search returns an unbounded number of results.
    ///                No page size limit — a large dataset or injected payload returns all rows,
    ///                consuming unbounded memory and producing arbitrarily large responses.
    /// "Fixed"      – Results are hard-capped at 50; response includes a truncated flag.
    /// </summary>
    public string BookingSearchResourceConsumption { get; set; } = "Fixed";

    /// <summary>
    /// Lab 06 – Race Condition (TOCTOU) on Coupon Redemption.
    /// "Vulnerable" – POST /api/coupons/redeem reads UsesCount, checks it, then delays 500 ms,
    ///                then increments. Concurrent requests all pass the check before any write,
    ///                allowing a single-use coupon to be redeemed more than once.
    /// "Fixed"      – Uses an atomic UPDATE … WHERE UsesCount &lt; MaxUses. Only one concurrent
    ///                request can increment; all others see 0 rows affected and receive 409.
    /// </summary>
    public string CouponRedemptionRaceCondition { get; set; } = "Fixed";

    /// <summary>
    /// Lab 07 – Race Condition (TOCTOU) on Password Reset Token.
    /// "Vulnerable" – POST /api/auth/reset-password reads the token, validates it, delays 500 ms,
    ///                then marks it used. Concurrent requests can both pass the check before either
    ///                writes, allowing a single-use token to reset the password more than once.
    /// "Fixed"      – Uses an atomic UPDATE … WHERE UsedAt IS NULL. Only one request wins;
    ///                the other sees 0 rows affected and receives 409.
    /// </summary>
    public string PasswordResetRaceCondition { get; set; } = "Fixed";

    /// <summary>
    /// Lab 08 – Server-Side Request Forgery (SSRF) on Webhook Test.
    /// "Vulnerable" – POST /api/webhooks/test fetches any caller-supplied URL, including
    ///                internal services, cloud metadata endpoints, and loopback addresses.
    /// "Fixed"      – Validates the URL: must be HTTPS, no private IP ranges, no localhost.
    /// </summary>
    public string WebhookSsrf { get; set; } = "Fixed";

    /// <summary>
    /// Lab 09 – Information Disclosure via Unhandled Exception Details.
    /// "Vulnerable" – Unhandled exceptions return the full exception message, type, source,
    ///                and stack trace in the HTTP response body.
    /// "Fixed"      – Returns a generic "An internal error occurred." message with no details.
    /// </summary>
    public string ExceptionDetailDisclosure { get; set; } = "Fixed";

    /// <summary>
    /// Lab 11 – Log Injection and Audit Log Manipulation.
    /// LogInjection:
    ///   "Vulnerable" – User-controlled values (username, details) are string-interpolated
    ///                  directly into ILogger messages. A \n in a username creates a fake
    ///                  log line that looks like a legitimate server entry.
    ///   "Fixed"      – Control characters are stripped before the value reaches ILogger.
    ///                  Structured logging placeholders are used so values are never interpreted
    ///                  as format strings or embedded raw.
    /// AuditLogDeletion:
    ///   "Vulnerable" – DELETE /api/audit-logs/{id} is accessible to both AdminUser and SupportUser
    ///                  with no secondary audit trail — a SupportUser can erase evidence of their actions.
    ///   "Fixed"      – Only AdminUser may delete entries; every deletion creates an immutable
    ///                  LOG_ENTRY_DELETED record so erasure itself is audited.
    /// </summary>
    public string LogInjection { get; set; } = "Fixed";
    public string AuditLogDeletion { get; set; } = "Fixed";

    /// <summary>
    /// Lab 12 – Brute Force MFA Protection.
    /// "Vulnerable" – POST /api/auth/mfa/verify places no limit on failed attempts.
    ///                A 4-digit OTP (10,000 combinations) can be enumerated with a simple loop.
    /// "Fixed"      – Attempt count is tracked; after 5 failures the challenge is invalidated
    ///                and the caller receives 429 Too Many Requests.
    /// </summary>
    public string MfaBruteForceProtection { get; set; } = "Fixed";

    /// <summary>
    /// Lab 10 – Sensitive Data Exposure: Credit Card PII Storage.
    /// "Vulnerable" – The full 16-digit card number is stored in the database and returned in
    ///                API responses. An IDOR or SQL injection exposes real card numbers.
    /// "Fixed"      – The server tokenizes on arrival: stores only the last 4 digits and an
    ///                opaque token (tok_…). The full number is never persisted or returned.
    /// </summary>
    public string CardPiiStorage { get; set; } = "Fixed";
}
