# Workshop: Claude Code Approval Decisions

Claude Code asks for your approval in two ways:
1. **Tool approval prompts** — a UI prompt before running a Bash command, editing a file, etc.
2. **Text questions** — Claude asks in its response before doing something impactful

This workshop uses real examples from a development session to teach when to approve, when to deny, and how to configure defaults.

---

## Part 1 — Tool Approval Prompts

These appear automatically when Claude wants to use a tool. You see the exact command before it runs.

### Category A — Read-only (almost always safe to approve)

Examples from this session:

| Command | Why it appeared |
|---------|----------------|
| `dotnet build --no-restore` | Verify code compiles |
| `grep -n "Authorize" Controllers/*.cs` | Find a pattern in code |
| `cat appsettings.json` | Read config |
| `lsof -ti:5000` | Check what's on a port |
| `PGPASSWORD=... psql ... -c "SELECT ..."` | Query the database |

**When to deny:** Almost never. Read operations have no side effects.

---

### Category B — Process management (pause and read)

Examples from this session:

| Command | What it does | Risk |
|---------|-------------|------|
| `pkill -f "BookingDojo.Api"` | Kills the API process | Drops active connections |
| `kill $(lsof -ti:5000)` | Kills whatever is on port 5000 | Could kill the wrong process |
| `dotnet run &>/tmp/api.log &` | Starts a background process | Hard to stop if something goes wrong |

**Questions to ask yourself:**
- Is the process it's killing the right one?
- Will killing it cause data loss (e.g. mid-transaction)?
- Do I know how to stop the background process if needed?

---

### Category C — File edits (read the diff)

Examples from this session:

| Action | File | Risk |
|--------|------|------|
| Edit | `Controllers/CouponsController.cs` | Logic change — could break the API |
| Edit | `appsettings.json` | Config change — affects all environments |
| Edit | `labs/06-race-condition-coupon.md` | Documentation change — low risk |
| Write (new file) | `Controllers/CouponsController.cs` | New code — review before approving |

**Questions to ask yourself:**
- Does the diff match what I asked for?
- Is it editing the right file?
- Would this change affect other features?

---

### Category D — Database operations (highest risk)

Examples from this session:

| Command | What it does | Risk |
|---------|-------------|------|
| `dotnet run -- --seed-and-exit` | Drops schema and re-seeds all data | **Destroys all existing data** |
| `DROP SCHEMA IF EXISTS bookingdojo CASCADE` | Deletes every table | **Irreversible without a backup** |
| `UPDATE ... SET "UsesCount"=0` | Modifies live data | Changes state permanently |

**Questions to ask yourself:**
- Is there a backup?
- Is this a dev database or production?
- Did I understand that "re-seed" means DROP + recreate?

**Best practice:** The model should tell you what the command does before you approve. If it doesn't, ask.

---

### Category E — Network and external calls

Examples from this session:

| Command | What it does | Risk |
|---------|-------------|------|
| `curl -X POST http://localhost:5001/...` | Hits a local API | Low — local only |
| `curl ... http://external-service/...` | Hits an external service | Could send real data, incur costs |

---

## Part 2 — Text Questions

These are questions Claude asks in its response before taking an action. They are lower-stakes because nothing happens until you reply.

### Real examples from this session

| Question | Category | Suggested answer |
|----------|----------|-----------------|
| "Want me to fix the `setup.sh` to use `docker compose` v2 syntax?" | File edit | Yes if you use v2, No if setup works fine |
| "Do you want me to check what partners are seeded?" | Read-only query | Yes — no risk |
| "Want me to add a `BookingDetailPage` at `/bookings/:id`?" | New feature | Depends on your roadmap |
| "Want me to go ahead and implement all of this?" | Large change | Ask for a summary first if scope is unclear |
| "Want me to go ahead?" | Vague — ask what "this" means | Clarify before answering |
| "Which direction do you want to go?" | Design choice | Always answer — Claude is blocked until you do |
| "Should MFA gate a specific sensitive action, or stand alone?" | Architecture | Think before answering — affects the whole lab |
| "Idea C is also interesting — want to add it too?" | Scope creep | Say No if you want to stay focused |

---

## Part 3 — Decision Framework

```
Is it reversible?
├── Yes → Approve freely (file edits with git, read-only queries)
└── No  → Is it on a dev/test system?
          ├── Yes → Approve with awareness (dropping dev DB is fine)
          └── No  → Deny and discuss (never drop prod data without backup)

Does it affect external systems?
├── No  → Approve
└── Yes → Read exactly what will be sent before approving

Is the scope clear?
├── Yes → Approve
└── No  → Ask Claude to explain before approving
```

---

## Part 4 — Pre-approving Safe Commands

To reduce repetitive approval prompts for commands you trust, add to `.claude/settings.json`:

```json
{
  "permissions": {
    "allow": [
      "Bash(dotnet build*)",
      "Bash(dotnet run*)",
      "Bash(npm run*)",
      "Bash(curl -s http://localhost*)",
      "Bash(grep*)",
      "Bash(find*)",
      "Bash(cat*)",
      "Bash(ls*)"
    ],
    "deny": [
      "Bash(rm -rf*)",
      "Bash(git push --force*)",
      "Bash(DROP*)"
    ]
  }
}
```

Pre-approve read-only and local-only commands. Keep destructive operations on manual approval.

---

## Part 5 — Red Flags

Pause and re-read before approving if Claude:

- Runs a command that **deletes or overwrites** without showing you what will be lost
- Edits a file you **didn't ask it to touch**
- Makes a change that affects **more files than expected**
- Asks a vague question like "Want me to go ahead?" — always ask "go ahead with what exactly?"
- Suggests restarting a service mid-operation without explaining why

---

## Key Takeaways

- **Read before approving** — especially for Bash commands and database operations
- **Reversibility is your guide** — git-tracked file edits are reversible; dropped databases are not
- **Vague questions need clarification** — "Want me to go ahead?" is not enough information
- **Pre-approve the boring stuff** — free yourself from approving `dotnet build` every time
- **Never approve production changes** from a dev session without an explicit confirmation step
