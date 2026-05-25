# Exercise 06 — Race Conditions: Coupon Redemption

**Difficulty:** Intermediate  
**Category:** Race Conditions / TOCTOU  
**OWASP Top 10:** API4:2023 — Unrestricted Resource Consumption / A04:2021 — Insecure Design

---

## Scenario

The coupon redemption endpoint (`POST /api/coupons/redeem`) enforces a per-coupon `MaxUses` limit by reading the current use count, checking it, waiting slightly, then writing the increment back. Because the check and the write are two separate operations with a gap between them, two concurrent requests can both pass the check before either writes — allowing a single-use coupon to be redeemed multiple times.

---

## Background

A **Time of Check / Time of Use (TOCTOU)** race condition occurs when:

1. A value is read and validated (Time of Check)
2. Some time passes during which another actor can change that value
3. The system acts based on the now-stale validation result (Time of Use)

The window between check and write is the **race window**. In a web API, this window is often only milliseconds — but that is enough for concurrent requests from the same user or multiple users to all pass the check simultaneously.

Common real-world examples:
- Coupon / discount code reuse
- Referral bonus double-claim
- Gift card balance bypass
- Double-spend in payment flows
- Inventory over-sell (booking a sold-out seat)

---
