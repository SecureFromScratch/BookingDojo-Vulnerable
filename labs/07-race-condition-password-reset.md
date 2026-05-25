# Exercise 07 — Race Conditions: Password Reset Token

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** A07:2021 — Identification and Authentication Failures

---

## Scenario

The password reset flow issues a single-use token. The token invalidation is implemented as check-then-mark: the server reads the token, confirms `UsedAt IS NULL`, waits (simulating real-world I/O), then marks it used and updates the password. Two concurrent requests with the same token can both pass the check before either writes — allowing an attacker who intercepts one reset token to use it twice: setting the password to one they control and locking the victim out.

---

## Background

Password reset tokens are a high-value target: a single stolen token can grant account takeover. The TOCTOU pattern is especially dangerous here because:

- The token is often transmitted over insecure channels (email, SMS)
- The race window can persist for hundreds of milliseconds in real implementations
- The victim does not know the token has been double-consumed — both write operations succeed silently

---
