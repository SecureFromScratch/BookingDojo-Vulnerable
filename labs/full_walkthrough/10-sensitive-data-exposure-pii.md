# Lab 10 – Sensitive Data Exposure: Credit Card PII Storage

## Learning objectives

- Understand why storing more sensitive data than necessary creates risk
- See how IDOR becomes far more dangerous when full PII is persisted
- Learn the tokenization pattern: accept, mask, discard — never store

## Background

BookingDojo accepts a credit card number when creating a booking. In the vulnerable version, the full 16-digit card number is stored in the `CartItems` table when items are added to the cart, and copied into the `Bookings` table when the cart is checked out. It is then returned in every API response. An IDOR vulnerability (Lab 02) or a SQL injection (Lab 03) then escalates immediately from "I can see your last 4 digits" to "I can steal your full card number."

---

## Step 1 – Observe the vulnerability

Log in as `partner / Partner1234!` at `http://localhost:5173` and go to **Bookings**.

Fill in the **New Booking** form with any hotel, dates, and this card number:

```
5500005555554242
```

Click **Book Now**. Open **DevTools → Network**, find the checkout response and inspect the JSON. You will see the full card number stored and returned:

```json
{
  "cardLastFour": "4242",
  "cardNumber": "5500005555554242",
  "cardToken": null
}
```

The full PAN (Primary Account Number — the 16-digit card number) is in the database and in the API response. Any vulnerability that lets an attacker read booking data (IDOR, SQL injection, DB dump) immediately yields a usable card number.

---

## Step 2 – The card numbers are already in every response

Log in and look at the booking list — every seeded booking has a full card number stored:

```bash
curl -s -c cookies.txt -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"partner","password":"Partner1234!"}' | jq .

curl -s -b cookies.txt http://localhost:5001/bff/bookings | jq '.results[] | {id, cardLastFour, cardNumber}'
```

Expected — every booking exposes its full card number:

```json
{ "id": 2,  "cardLastFour": "4242", "cardNumber": "5500005555554242" }
{ "id": 3,  "cardLastFour": "0001", "cardNumber": "4111111111110001" }
{ "id": 4,  "cardLastFour": "0002", "cardNumber": "4111111111110002" }
...
```

The full PAN is stored and returned for every booking. Any vulnerability that exposes booking data hands the attacker usable card numbers.

This means any vulnerability that exposes booking data — IDOR (Lab 02), SQL injection (Lab 03), a DB dump, a misconfigured export endpoint — immediately hands the attacker usable card numbers. Without full PAN storage, the same vulnerabilities would expose only last-4 digits, which are not usable for fraud.

---

## Step 3 – Apply the fix

The UI sends the card number when adding an item to the cart (`POST /api/cart/items`). That is where the full number first touches the database — so that is where it must be discarded.

**File:** `src/BookingDojo.Api/Controllers/CartController.cs`

Find `AddItem`. The vulnerable code assigns the full card number to a local variable and stores it in the `CartItem`:

**Vulnerable — these two lines:**

```csharp
var lastFour = request.CardNumber[^4..];
var storedCardNumber = request.CardNumber;
```

**Replace `storedCardNumber` assignment with:**

```csharp
var lastFour = request.CardNumber[^4..];
var storedCardNumber = (string?)null;
```

That single change is the entire fix. `storedCardNumber` is assigned to `CardNumber` on the `CartItem` two lines later — so setting it to `null` means the full PAN is never written to the database. When the cart is checked out, the `Booking` copies `CardNumber` from the `CartItem`, which is now `null`.

The `lastFour` variable is kept — the last 4 digits are harmless and needed for display.

**The fix addresses new bookings going forward. Historical bookings will need to be handled separately by the DBA.**
 
---

## Step 4 – Verify

Restart the API so the change is compiled, then create a new booking with the same card number and check the bookings list:

```bash
curl -s -b cookies.txt http://localhost:5001/bff/bookings | jq '.results[0] | {id, cardLastFour, cardNumber, cardToken}'
```

Expected — the new booking shows `null` for `cardNumber`:

```json
{
  "id": 216,
  "cardLastFour": "4242",
  "cardNumber": null,
  "cardToken": null
}
```

The full card number is no longer stored. `cardToken` is also `null` — this application does not integrate with a real payment gateway, so no token is generated. That is intentional: **the important thing is that the full PAN is gone**. In a production system you would call a payment processor (Stripe, Braintree, etc.) before this point, receive an opaque token, and store that instead.

---

## How it works at runtime

```
POST /api/cart/items (CardNumber = "5500005555554242")
        │
        ▼
CartController.AddItem()
        │
        ├─ Vulnerable: storedCardNumber = request.CardNumber
        │       │
        │       ▼
        │  CartItem.CardNumber = "5500005555554242"
        │       │
        │  POST /api/cart/checkout
        │       │
        │       ▼
        │  Booking.CardNumber = "5500005555554242"   ← full PAN in DB
        │       │
        │  attacker fetches GET /bff/bookings (IDOR or SQL injection)
        │       └─► { "cardNumber": "5500005555554242" }  ← stolen
        │
        └─ Fixed: storedCardNumber = null
                │
                ▼
           CartItem.CardNumber = null
                │
           POST /api/cart/checkout
                │
                ▼
           Booking.CardNumber = null                ← nothing stored
                │
        attacker fetches GET /bff/bookings
                └─► { "cardNumber": null }          ← nothing to steal
```

## But wait — if the card number isn't stored, how does the charge happen?

The short answer: your server never needs to store the number to charge the card. Real systems use a **payment processor** (Stripe, Braintree, Adyen, etc.) as an intermediary:

```
User enters card number
        │
        ▼
Your server calls Stripe API with the full number
        │
        ▼
Stripe stores the card on their PCI-compliant infrastructure
and returns an opaque token:  tok_abc123def456
        │
        ▼
Your server stores only the token + last-4 digits
(the full number is never written to your DB)
        │
        ▼
When you need to charge the customer:
Your server calls Stripe with the token → Stripe does the charge
The card number never comes back to you
```

The token is just a reference that lets the payment processor find the card. It is useless to an attacker — they cannot reverse it into a card number, and they cannot use it directly to make payments at other merchants.

BookingDojo has no real payment gateway, so it skips the Stripe step and generates a fake placeholder token. In a real deployment you would call the processor before writing to the database.

---

## Key takeaways

| Principle | Detail |
|---|---|
| Collect minimum data | If you only need last-4 for display, never store the full number |
| Discard at the boundary | Full number touches only `AddItem` — never EF Core, never the DB |
| Tokenize with a processor | A real system calls Stripe/Braintree first, gets a token, stores that instead |
| Defence in depth | Removing PAN storage limits the blast radius of every other vulnerability (IDOR, SQLi, DB dump) |
| PCI DSS scope | Storing full PANs puts your entire application in PCI DSS scope; tokens are out of scope |
