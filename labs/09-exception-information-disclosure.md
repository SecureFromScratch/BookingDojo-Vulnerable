# Exercise 09 — Information Disclosure via Exception Details

**Difficulty:** Beginner  
**Category:** Information Disclosure / Security Misconfiguration  
**OWASP Top 10:** A05:2021 — Security Misconfiguration

---

## Scenario

When an unhandled exception occurs in the API, the server returns the full exception details — message, type, source assembly, and stack trace — directly in the HTTP response body. The exception message in this lab contains a fake database connection string with credentials. In a real system this pattern leaks internal architecture, file paths, framework versions, and credentials embedded in error messages.

---

## Background

**Information disclosure** via exception details is a misconfiguration that is common when:

- A development exception page (`UseDeveloperExceptionPage`) is left enabled in production
- A custom exception handler returns the raw `Exception.Message` / `Exception.StackTrace`
- Error logging middleware echoes exception details back to the client
- A catch-all returns `ex.ToString()` as the response body

What leaks:
- **Stack trace** — internal file paths, class names, method names, line numbers; reveals architecture and attack surface
- **Exception type** — `System.Data.SqlException` confirms SQL Server; `NpgsqlException` confirms PostgreSQL; `FileNotFoundException` reveals filesystem paths
- **Exception message** — may contain connection strings, credentials, SQL queries, file paths
- **Source assembly** — reveals framework versions, internal project names

---
