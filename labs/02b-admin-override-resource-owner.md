# Bonus Lab 02b — Admin Override: Resource-Based Authorization

**Difficulty:** Intermediate  
**Category:** Broken Access Control / Authorization Design  
**OWASP Top 10:** A01:2025 — Broken Access Control

---

## Scenario

In Lab 02 you fixed IDOR by adding a `ResourceOwner` policy to `GET /api/bookings/{id}`. The ownership check works — but now the support team reports a problem: when an admin tries to look up a customer's booking to resolve a dispute, they receive **403 Forbidden**.

The task: extend the authorization so that **the resource owner OR an admin** can access any booking, while keeping the deny-by-default for everyone else.

---

## Background

### Where Lab 02 left off

```
src/BookingDojo.Api/
├── Authorization/
│   ├── ResourceOwnerRequirement.cs    ← requirement + handler
│   └── AuthorizationExtensions.cs     ← AddAuthorizationPolicies() extension method
└── Controllers/
    └── BookingsController.cs          ← GetBookingById uses [Authorize(Policy = "ResourceOwner")]
```

`ResourceOwnerAuthorizationHandler` only has one path to success — it checks `booking.UserId == JWT sub`. Admin is just another role; it conveys no special privilege here.

---
