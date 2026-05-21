# Lab 10 – Sensitive Data Exposure: Credit Card PII Storage

**Difficulty:** Intermediate  
**Category:** Sensitive Data Exposure / PII Storage  
**OWASP Top 10:** A02:2021 — Cryptographic Failures  
---

## Learning objectives

- Understand why storing more sensitive data than necessary creates risk
- See how IDOR becomes far more dangerous when full PII is persisted
- Learn the tokenization pattern: accept, mask, discard — never store

## Background

BookingDojo accepts a credit card number when creating a booking or adding an item to the cart.

In **Vulnerable** mode the full 16-digit card number is stored in the `Bookings` table and returned in every API response that includes the booking. An IDOR vulnerability (Lab 02) or a SQL injection (Lab 03) escalates immediately from "I can see your last 4 digits" to "I can steal your full card number."

In **Fixed** mode the server tokenizes on arrival:
1. Extract the last 4 digits.
2. Generate an opaque token (`tok_xxxxxxxxxxxx`).
3. Store only the last 4 digits and the token. The full number is never written to the database.

---

## Step 1 – Create a booking and observe the response via the UI

Log in as `partner / Partner1234!` at `http://localhost:5173` and go to **Bookings**.

Fill in the **New Booking** form with any hotel, dates, and this card number:

```
5500005555554242
```

Click **Book Now** — you're redirected to the Cart. In the cart table, look at the **Card** column:

- **Vulnerable mode:** the full 16-digit number `5500005555554242` is shown in **red**
- **Fixed mode:** `**** **** **** 4242` and a token like `tok_a3f9c2d1e8b6` in green

After checkout, go to **Bookings → My Bookings** and click the booking row to open the detail page. In Vulnerable mode the full card number is returned in the API response and rendered in red in the UI.

To observe the raw API response, open **DevTools → Network**, click the booking detail request (`/bff/bookings/{id}`), and inspect the response JSON — in Vulnerable mode you'll see `"cardNumber": "5500005555554242"`.

Or with curl:

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq -r '.token')

curl -s http://localhost:5000/api/bookings \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "hotelId": "<any-hotel-id>",
    "checkIn": "2026-08-01",
    "checkOut": "2026-08-05",
    "cardNumber": "5500005555554242",
    "specialRequests": ""
  }' | jq '{id, cardLastFour, cardNumber, cardToken}'
```

**Vulnerable response:**
```json
{
  "id": 215,
  "cardLastFour": "4242",
  "cardNumber": "5500005555554242",
  "cardToken": null
}
```

**Fixed response:**
```json
{
  "id": 215,
  "cardLastFour": "4242",
  "cardNumber": null,
  "cardToken": "tok_a3f9c2d1e8b6"
}
```

---

## Step 2 – Combine with IDOR to steal another user's card

The seeded data already has two meaningful bookings:
- Booking **#1** — admin's card: `4111111111111234`
- Booking **#2** — partner's card: `5500005555554242`

Log in as **partner** and request admin's booking:

```bash
curl -s http://localhost:5000/api/bookings/1 \
  -H "Authorization: Bearer $TOKEN" | jq '{id, username, cardNumber}'
```

Expected (with both labs vulnerable):
```json
{
  "id": 1,
  "username": "admin",
  "cardNumber": "4111111111111234"
}
```

Without the PII lab (cardNumber stored as last-4 only), IDOR exposes `"1234"` — annoying but not catastrophic. With the PII lab, IDOR hands the attacker a usable card number.

---

## Step 3 – Combine with SQL injection to dump all card numbers

Switch `BookingSearchSqlInjection` to `"Vulnerable"` and `CardPiiStorage` to `"Vulnerable"`.

The search endpoint concatenates the query into raw SQL. The `Bookings` table now has a `CardNumber` column. A UNION SELECT can extract it directly:

```bash
# Discover the CardNumber column
curl -g "http://localhost:5000/api/bookings/search?q=%25'%20UNION%20SELECT%201,'00000000-0000-0000-0000-000000000000','dump','00000000-0000-0000-0000-000000000001','4111','2020-01-01','2020-01-02','VISA-DUMP',null,null,'',now()--" \
  -H "Authorization: Bearer $TOKEN" | jq '.results[].cardLastFour'
```

*(Adjust column count to match the current SELECT — see Lab 03 for the column enumeration steps.)*

---

## The fix

The fix stores only a tokenized reference instead of the full card number. The API response has `"cardNumber": null` regardless of who is fetching the booking. Even with IDOR, the attacker gets:

```json
{
  "id": 1,
  "username": "admin",
  "cardLastFour": "1234",
  "cardNumber": null,
  "cardToken": "tok_a3f9c2d1e8b6"
}
```

The token is useless to the attacker — it has no card data. The real system would store the token with a PCI-compliant payment processor and use it only server-to-server.

---

### How it works at runtime

```
POST /api/bookings (CardNumber = "4111111111111234")
        │
        ▼
BookingsController.CreateBooking()
        │
        ├─ Vulnerable: full card number stored in DB
        │       │
        │       ▼
        │  DB: CardNumber = "4111111111111234"
        │       │
        │  attacker fetches GET /api/bookings/1 (via IDOR or SQL injection)
        │       └─► { "cardNumber": "4111111111111234" }  ← full PAN exposed
        │
        └─ Fixed: card tokenised at write time, number never stored
                │
                ▼
           DB: CardNumber = null, CardToken = "tok_a3f9c2d1e8b6"
                │
        attacker fetches GET /api/bookings/1
                └─► { "cardNumber": null, "cardToken": "tok_a3f9c2d1e8b6" }
                    token is opaque — no card data to steal
```

## Key takeaways

| Principle | Detail |
|---|---|
| Collect minimum data | If you only need last-4 for display, never store the full number |
| Tokenize at the boundary | Full number touches only the tokenization function — never EF Core, never the DB |
| Defence in depth | Tokenization makes every other vulnerability (IDOR, SQLi, DB dump) much less impactful |
| PCI DSS scope | Storing full PANs (Primary Account Numbers) puts your entire application in PCI DSS scope; tokens are out of scope |
