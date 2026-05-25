# Exercise 05 — Uncontrolled Resource Consumption: Booking Search

**Difficulty:** Beginner  
**Category:** Uncontrolled Resource Consumption  
**OWASP Top 10:** A05:2021 — Security Misconfiguration / API4:2023 — Unrestricted Resource Consumption

---

## Scenario

The booking search endpoint (`GET /api/bookings/search?q=&pageSize=N`) accepts a caller-supplied `pageSize` and honours it without any server-side cap. An attacker can omit `pageSize` entirely (returns every row) or set it to `999999` to retrieve the full table in a single request. Combined with Lab 03's SQL injection, the same request can return **all bookings for all users** with no limit — loading arbitrarily large result sets into server memory.

---

## Background

**Uncontrolled resource consumption** occurs when an API does not enforce limits on the resources a single request can consume. Common forms include:

- No maximum page size — one request returns millions of rows
- No execution timeout — slow queries hold connections indefinitely
- No rate limiting — any user can send unlimited requests per second
- No request size limit — uploading arbitrarily large payloads

This vulnerability is distinct from SQL injection. Even with a perfectly parameterised query, returning 100 000 rows per request is a DoS risk. Both vulnerabilities exist independently on this endpoint; the workshop toggles them separately.

---
