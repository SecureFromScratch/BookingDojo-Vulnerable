# Exercise 04 — Time-Based Blind SQL Injection: Login

**Difficulty:** Intermediate  
**Category:** Injection  
**OWASP Top 10:** A03:2021 — Injection

---

## Scenario

The login endpoint returns only two outcomes: `200 OK` with a token, or `401 Unauthorized`. No query data is ever reflected in the response. A classic in-band injection (Lab 03) would be useless here — there is nothing to read.

But the endpoint is still vulnerable. You will use **response timing** as a side-channel to extract the administrator's BCrypt password hash from the `Users` table — a column that is never returned by any API endpoint.

---

## Background

**Time-based blind SQL injection** works by injecting a conditional sleep into the database query:

```sql
CASE WHEN (condition) THEN pg_sleep(3) ELSE pg_sleep(0) END
```

- Response takes ~3 seconds → condition was **true**
- Response is immediate → condition was **false**

By testing conditions character by character, an attacker can reconstruct any string in the database. The HTTP response body is irrelevant — the clock is the oracle.

**What the attacker gains here:** the admin's BCrypt password hash. This column is inaccessible through every other endpoint. Once extracted, it can be cracked offline with hashcat. A password like `Admin1234!` appears in common wordlists and cracks in seconds.

> **Auth flow context:** The login endpoint is on the BFF, which proxies credentials to the API. The API issues a JWT that is encrypted into an `httpOnly` cookie by the BFF — so the JWT itself is never in the browser. See [docs/jwt.md](../docs/jwt.md).

---
