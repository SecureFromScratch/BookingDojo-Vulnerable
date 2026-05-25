# Lab 12 — Audit Log Manipulation

**Category:** Logging & Monitoring Failures (OWASP A09:2021)  
**Difficulty:** Intermediate  
**Two sub-labs:** A) CRLF Log Injection · B) Audit Log Deletion

---

## Background

Audit logs are a critical security control: they provide the evidence trail needed to detect attacks, investigate incidents, and hold users accountable. This lab covers two ways attackers undermine that control:

- **Log Injection (CRLF)** — injecting newlines into log messages to forge fake log lines  
- **Audit Log Deletion** — abusing over-permissive delete endpoints to silently erase evidence

---
