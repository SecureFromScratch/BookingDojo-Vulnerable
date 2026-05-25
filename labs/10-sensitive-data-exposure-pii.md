# Lab 10 – Sensitive Data Exposure: Credit Card PII Storage

## Learning objectives

- Understand why storing more sensitive data than necessary creates risk
- See how IDOR becomes far more dangerous when full PII is persisted
- Learn the tokenization pattern: accept, mask, discard — never store

## Background

BookingDojo accepts a credit card number when creating a booking. In the vulnerable version, the full 16-digit card number is stored in the `CartItems` table when items are added to the cart, and copied into the `Bookings` table when the cart is checked out. It is then returned in every API response. An IDOR vulnerability (Lab 02) or a SQL injection (Lab 03) then escalates immediately from "I can see your last 4 digits" to "I can steal your full card number."

---
