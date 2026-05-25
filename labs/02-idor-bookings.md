# Exercise 02 — IDOR: Booking ID Enumeration

**Difficulty:** Beginner  
**Category:** Broken Access Control / IDOR  
**OWASP Top 10:** A01:2021 — Broken Access Control

---

## Scenario

You have just made a hotel booking on BookingDojo. Your confirmation shows **Booking #3**.

You notice the ID is a small sequential integer. You wonder: what happens if you request **Booking #1** or **Booking #2**?

---

## Background

**IDOR (Insecure Direct Object Reference)** occurs when an application exposes a direct reference to an internal object — such as a database row — and does not verify that the requesting user is authorised to access it.

Authentication answers *"who are you?"*. Authorisation answers *"are you allowed to do this?"*.  
Being logged in does not automatically mean you can access every resource. Each object access needs its own ownership check.

Sequential integer IDs make IDOR trivially exploitable: an attacker who sees their own ID can enumerate adjacent IDs to find other users' data.

---
