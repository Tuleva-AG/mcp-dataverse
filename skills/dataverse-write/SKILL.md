---
name: dataverse-write
description: Create or update Dataverse records via the mcp-dataverse MCP server. Covers INSERT/UPDATE SQL, the mandatory two-phase approval gate (preview + ConfirmWrite), and safety rules. Use when the user wants to create or update Dataverse data.
---

# Dataverse Write

Create/update records via SQL through the `mcp-dataverse` MCP server. Every write passes a **server-enforced approval gate** — nothing is written without explicit user approval.

## Before writing

Call `Connect` once at session start (interactive login on first use). A write attempt on an unauthenticated connection blocks on a login prompt instead of returning a preview.

`ExecuteSQL` accepts `bypassCustomPlugins` (default `false`). It skips registered plugin steps and
real-time workflows during INSERT/UPDATE - only system administrators can do this, and it can break
business logic. Only set it to `true` when the user explicitly asks for it; the preview shows the
flag, and the executed write reports it again.

## The gate (always two calls)

1. **Preview**: send your `INSERT`/`UPDATE` to `ExecuteSQL`. The server returns
   `<write_preview>` with target table, row estimate, the statement, and a `confirm_token`.
   **Nothing has been written yet.**
2. **Show the user** the preview (table, statement, estimated rows) and ask for explicit approval.
3. **Execute**: only after the user approves, call `ConfirmWrite` with the `confirm_token`.
   Tokens are single-use and expire after 5 minutes — if expired, re-run `ExecuteSQL`.

Never call `ConfirmWrite` without prior user approval. Never batch-approve multiple previews.

## Rules enforced by the server

- **INSERT/UPDATE only** — `DELETE` is rejected outright.
- **UPDATE requires WHERE** — an UPDATE without a WHERE clause is rejected (would rewrite every row).
- **Single statement per call** — no `;`-separated batches.
- Writes run under the authenticated user's identity (see server auth docs).

## Examples

Update:
```sql
UPDATE contact SET emailaddress1 = 'new@example.com' WHERE contactid = 'GUID-here'
```

Insert (prefer naming columns explicitly):
```sql
INSERT INTO contact (firstname, lastname, emailaddress1) VALUES ('Max', 'Mustermann', 'max@example.com')
```

After `ConfirmWrite`, the server reports `<affected_rows>` plus `<record_links>` (for UPDATEs) -
the Dataverse URLs of the affected records, ready to share with the user. For INSERTs the server
cannot know the new id; verify with a follow-up SELECT and share the link from there.

## Verify before you write

Before an UPDATE, preview the affected rows first:
```sql
SELECT contactid, emailaddress1 FROM contact WHERE emailaddress1 = 'old@example.com'
```
Use the same WHERE clause for the UPDATE. Compare row counts.
