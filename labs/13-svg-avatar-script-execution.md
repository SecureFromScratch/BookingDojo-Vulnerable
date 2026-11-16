# Exercise 13 — JavaScript Execution via SVG Avatar Upload

**Difficulty:** Beginner  
**Category:** Injection / Cross-Site Scripting  
**OWASP Top 10:** A05:2025 — Injection

---

## Scenario

BookingDojo lets users upload a profile picture. You notice that the upload accepts SVG files, not just familiar image formats such as PNG and JPEG.

In the `labs` folder, you'll find `sfs-avatar-script-test.svg`, a harmless test image containing JavaScript.

To explore the upload:

1. Sign in to BookingDojo.
2. Select **PROFILE** from the top navigation bar to open **My Profile**.
3. Scroll to **Profile Picture**, below the **Edit Profile** form.
4. Select **Upload File** (not **Fetch from URL**).
5. Click **Choose File**, select `labs/sfs-avatar-script-test.svg`, and submit the upload using the page's upload control.
6. Look at the avatar displayed next to your account details at the top of the profile page.

Explore what happens when the uploaded SVG appears as your avatar. Then inspect the avatar's URL and consider what might happen if someone opens that URL directly in a browser tab rather than viewing it as an image inside the profile page.

Your goal: determine whether an uploaded image can cause JavaScript to execute, and identify which browser rendering context makes the difference.

---

## Background

**SVG (Scalable Vector Graphics)** is an XML-based image format. Unlike passive image formats such as PNG, JPEG, or WebP, SVG can contain JavaScript.

Browsers treat an SVG differently depending on how it is loaded:

1. When used as an HTML `<img>` source, SVG is rendered in an image context, where scripts are disabled.
2. When opened directly as a standalone SVG document, its JavaScript may execute.
3. A Base64 `data:image/svg+xml` URL still describes an SVG document; encoding its contents does not sanitize them or remove their active capabilities.

A `data:` document normally has an opaque (`null`) origin, limiting access to other origins' data. That isolation does **not** by itself prevent JavaScript execution, network requests, or CPU-intensive activity.

> **Exploration hint:** Pay attention to the difference between seeing the avatar on the profile page and opening the stored avatar URL directly. The supplied SVG is intended to demonstrate behavior without stealing information or modifying application data.

---
