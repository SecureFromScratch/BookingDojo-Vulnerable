# Lab 11 – Brute Force MFA Protection

## Learning objectives

- Understand why short OTPs without rate limiting are trivially enumerable
- See how a 4-digit code (10,000 possibilities) can be brute-forced in seconds
- Learn the mitigation: invalidate the challenge after N failed attempts

## Background

Multi-factor authentication adds a second proof of identity beyond a password.
A common implementation generates a short numeric code (OTP) that is delivered
out-of-band (SMS, email, authenticator app) and must be submitted within a short window.

In BookingDojo, **checkout requires MFA**. Before a payment is processed, the server
checks the JWT for an `mfa_verified_at` claim issued within the last 5 minutes. If the
claim is absent or stale, the checkout endpoint returns `403 { "requiresMfa": true }`.

This is a realistic pattern — banks call it "step-up authentication": low-risk actions
(browse, search) require only a password; high-risk actions (pay, transfer) require a
second factor.

The weakness: if the verification endpoint does not limit failed attempts, an attacker
who already has a session (e.g., stolen cookie) can enumerate all possible codes. For a
4-digit OTP that is only 10,000 requests — trivially fast.

---
