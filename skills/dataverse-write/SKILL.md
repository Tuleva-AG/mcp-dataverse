---
name: dataverse-write
description: Create or update Dataverse records via the mcp-dataverse MCP server. Covers INSERT/UPDATE SQL, the mandatory two-phase approval gate (preview + ConfirmWrite), and safety rules. Use when the user wants to create or update Dataverse data.
---

# Dataverse Write

Create/update records via SQL through the `mcp-dataverse` MCP server. Every write passes a **server-enforced approval gate** — nothing is written without explicit user approval.

## Before writing

Before every MCP call, inspect the exact tool schema with `mcp describe`. Never infer tool names, parameter names, or payload casing from this document or from a tool list. Tool lists may show names only; `describe` is authoritative.

Call `Connect` once at session start (interactive login on first use). A write attempt on an unauthenticated connection blocks on a login prompt instead of returning a preview.

`ExecuteSQL` accepts `bypassCustomPlugins` (default `false`). It skips registered plugin steps and
real-time workflows during INSERT/UPDATE - only system administrators can do this, and it can break
business logic. Only set it to `true` when the user explicitly asks for it; the preview shows the
flag, and the executed write reports it again.

## The gate (always two calls)

1. **Preview**: inspect `ExecuteSQL` with `mcp describe`, then send exactly this payload shape:

   ```json
   {
     "sqlQuery": "INSERT or UPDATE statement",
     "bypassCustomPlugins": false
   }
   ```

   The server returns `<write_preview>` with target table, row estimate, the statement, and a
   `confirm_token`. **Nothing has been written yet.**
2. **Show the user** the preview (table, statement, estimated rows) and ask for explicit approval.
3. **Execute**: after approval, inspect `ConfirmWrite` with `mcp describe`, extract the
   `confirm_token` value from the preview, and send exactly:

   ```json
   {
     "token": "<confirm_token from ExecuteSQL>"
   }
   ```

   The tool parameter is `token`, not `confirm_token`. Tokens are single-use and expire after
   5 minutes — if expired, re-run `ExecuteSQL`.

Never call `ConfirmWrite` without prior user approval. Never batch-approve multiple previews.

The gate is on by default. A server started with `DATAVERSE_APPROVAL_GATE=off` executes
INSERT/UPDATE immediately (response contains `<write_executed>`, no confirm token) - do not
call `ConfirmWrite` in that mode. Safety rules (no DELETE, UPDATE requires WHERE, single
statement) still apply.

## Field selection workflow (always, before composing the statement)

1. **Fetch field metadata first (mandatory).** Inspect `GetFieldMetadataByTableName` with
   `mcp describe`, then call it for the target table. For INSERT only use fields with
   `isvalidforcreate = 1`; for UPDATE only fields with `isvalidforupdate = 1`. Determine required
   fields only from properties actually returned by the metadata schema. Never invent or assume a
   property such as `isrequiredbyform` without schema evidence.
2. **Set only the requested fields.** Map the user's request to logical field names from the
   metadata. Never add "helpful" extra fields the user did not ask for.
3. **Ambiguous fields -> ask, don't guess.** If the user's wording (e.g. "email", "phone",
   "name") matches several fields, list the candidates and let the user pick. Typical collisions:
   `emailaddress1/2/3`, `telephone1/mobilephone/fax`, `firstname`/`fullname`/`nickname`,
   standard fields vs. similarly named custom fields (often prefixed like `new_` or `cr123_`).
   Ask before writing - never silently pick one.

Example flow:
```sql
-- 1. check candidate fields and their create/update validity
SELECT logicalname, isvalidforcreate, isvalidforupdate, isnullable
FROM metadata.attribute WHERE attribute.entitylogicalname = 'contact'
  AND (logicalname LIKE '%email%' OR logicalname LIKE '%phone%')
-- 2. ask the user which field to use if several match
-- 3. only then compose the INSERT/UPDATE and run it through the gate
```

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
