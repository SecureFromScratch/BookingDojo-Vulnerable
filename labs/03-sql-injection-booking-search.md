# Exercise 03 — SQL Injection: Booking Search

**Difficulty:** Beginner  
**Category:** Injection  
**OWASP Top 10:** A05:2025 — Injection

---

## Scenario

BookingDojo has a **Search Bookings by Hotel** feature on the Bookings page. You type a hotel name and see your matching bookings.

The search is backed by a database query. If that query is built by concatenating user input into a SQL string instead of using parameters, an attacker can break out of the intended query structure and read data that belongs to other users.

---

## Background

**SQL Injection** occurs when untrusted input is embedded directly into a SQL statement. The database cannot distinguish between the developer's intent and the attacker's payload — it executes whatever SQL it receives.

The classic defence is **parameterised queries**: the query structure is sent to the database first, and user input is bound separately as typed values. The database never interprets the input as SQL syntax.

Modern ORMs like Entity Framework Core use parameterised queries by default. The vulnerability re-appears when developers bypass the ORM to write raw SQL for performance or convenience — and forget to parameterise.

---
