# Exercise 01 — Stored XSS in Audit Logs

**Difficulty:** Beginner  
**Category:** Injection / Cross-Site Scripting    
**OWASP Top 10:** A05:2025 — Injection


---

## Scenario

You are a security researcher reviewing BookingDojo's admin dashboard. The application stores an audit log for every user action and displays it in the admin interface. You notice the audit log viewer renders the "Details" column differently from the rest of the table.

Your goal: inject a payload that executes JavaScript in the browser of any admin or support user who views the audit log.

---

## Background

**Stored XSS** (also called Persistent XSS) occurs when:
1. An attacker stores malicious HTML or JavaScript in the database via a legitimate-looking input
2. The application later retrieves that data and renders it in the browser without sanitizing it
3. The JavaScript executes in the victim's browser — with access to their cookies, DOM, and session

Stored XSS is considered more dangerous than Reflected XSS because the payload executes automatically for every user who views the affected page, without requiring the victim to click a crafted link.

> **Note on session tokens:** BookingDojo stores the JWT inside an `httpOnly` cookie, so `document.cookie` cannot expose it. XSS payloads here target the UI (defacement, keylogging, redirects) rather than direct token theft. See [docs/jwt.md](../docs/jwt.md) for how the cookie protection works.

---
