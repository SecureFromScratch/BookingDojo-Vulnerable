# Exercise 14 — Mass Assignment: Profile Role Escalation

**Difficulty:** Beginner  
**Category:** Broken Access Control / Mass Assignment  
**OWASP Top 10:** A01:2021 — Broken Access Control

---

## Scenario

BookingDojo lets users update their display name and bio. As a support user, you should be able to edit your own profile, but not your authorization level.

Can you make the profile update API change something that the **Edit Profile** form never offers?

---

## Background

A user interface is not an authorization boundary. The browser decides which fields to display, but an HTTP client can submit a different JSON body.

Some APIs take a flexible dictionary of incoming properties and apply them directly to the database model. This can turn a routine profile update into **mass assignment** (also called *over-posting*): the client can set properties it was never supposed to control.

---

## Explore

1. Open BookingDojo and log in as `support / Support1234!`.
2. Open **Developer Tools → Network**. Select **PROFILE** from the top navigation bar.
3. Find the request that retrieves your profile. Inspect its response. Which user fields are returned? Are all of them editable through the form?
4. Look at the role badge at the top right, **third item from the right**. What does it say?
5. On **My Profile**, change **Display Name** and click **Save Profile**.
6. Inspect the outgoing update request and the server response. Which properties does the client send, and which properties come back?
7. Consider whether the server would reject a property that is not present in the form. How could you test this with Developer Tools?
8. Can you change your role? If so, which parts of the interface change after signing out and back in?

Only use your own workshop account. Restore its original role when finished.

---

## Questions

- What is the difference between hiding a property in the UI and preventing its update on the server?
- Why might an API that accepts arbitrary JSON property names be convenient to implement?
- Where should the boundary be drawn between editable profile data and security-sensitive user data?
- How would you verify that an unauthorized change is rejected, not merely hidden in the UI?

> This is the discovery exercise. The full walkthrough contains the exact request edit, code-level explanation, fix, and verification.
