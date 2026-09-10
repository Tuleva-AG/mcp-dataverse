---
name: profile-project-manage
description: Create and update employee profile projects (Profilprojekte) in Dataverse via the mcp-dataverse MCP server. Use when the user wants to add a new project to their Mitarbeiterprofil, edit an existing profile project, update role/tasks/period/tools of a project entry, or sync a project description into the tu_employeeprofileproject table. Covers INSERT and UPDATE against tu_employeeprofileproject with correct field mapping and approval-gate handling.
---

# Profile Project Manage (Dataverse)

Create and update employee profile projects in the Dataverse `tu_employeeprofileproject` table using the `mcp-dataverse` MCP tools. This is the persistence layer behind the employee profile (Mitarbeiterprofil) project list.

## When to Use

- The user wants to add a new project to their Mitarbeiterprofil / profile project list
- The user wants to edit an existing profile project (role, tasks, period, tools, industry)
- The user wants to update a project entry after a project description was drafted
- The user wants to sync a project description into Dataverse

## When Not to Use

- The user only wants a *text* project description (no persistence) — use `employee-profile-project-description` instead
- The user wants to change the employee master data (tu_employee) — that is a different table
- The user wants to manage skills (tu_employeeskills) or documents — separate tables

## Prerequisites

- The `mcp-dataverse` MCP server must be available (tools `mcp-dataverse_*`).
- A Dataverse connection must be established: call `mcp-dataverse_Connect` once per session before any query/write.
- Environment: `https://tuleva-apps.crm4.dynamics.com` (delegated auth, token cached).

## Target Table: tu_employeeprofileproject

| Column | Display | Type | Notes |
|--------|---------|------|-------|
| `tu_employeeprofileprojectid` | Employee Profile Project | GUID (PK) | Primary key; needed for UPDATE |
| `tu_employee` | Mitarbeiter | Lookup → tu_employee | Required; the owning employee |
| `tu_name` | Name | Text | Project title (DE) — required |
| `tu_name_en` | Name (EN) | Text | English title — required |
| `tu_projectperiod` | Projektzeitraum | Text | e.g. `08.2026 - 09.2026` — required |
| `tu_projectperiod_en` | Project Period (EN) | Text | English period — required |
| `tu_industry` | Branche | Text | e.g. `IT / Personalwesen (Dokumentenmanagement)` — required |
| `tu_industry_en` | Industry (EN) | Text | English industry — required |
| `tu_role` | Rolle | Text | e.g. `Architekt, Developer` — required |
| `tu_role_en` | Role (EN) | Text | English role — required |
| `tu_tasksandactivities` | Aufgaben / Tätigkeiten | Text | Semicolon-separated compact list — required |
| `tu_tasksandactivities_en` | Tasks / Activities (EN) | Text | English tasks — required |
| `tu_toolsandmethodsused` | Werkzeuge / Methoden | Text | Comma-separated stack line — required |
| `tu_toolsandmethodsused_en` | Werkzeuge / Methoden (EN) | Text | English tools — required |
| `tu_sortdate` | Sort Datum | Date | First day of project end month (e.g. `2026-09-01`) |
| `tu_customer` | Kunde | Lookup → account | Optional |

**Every profile project must be stored in German AND English.** Always write both the DE field and its `_en` counterpart. All text values must be ASCII-safe — no umlauts (`ae`/`oe`/`ue`/`ss` instead of `ä`/`ö`/`ü`/`ß`).

## Workflow

### Step 1: Connect

Call `mcp-dataverse_Connect` first. If it fails, stop and report — do not attempt writes without a live connection.

### Step 2: Resolve the employee

Find the owning employee in `tu_employee` (by name or systemuser):

```sql
SELECT tu_employeeid, tu_name, tu_hrworksemployeeno
FROM tu_employee
WHERE tu_name LIKE '%Leclaire%'
```

Use the returned `tu_employeeid` GUID as the `tu_employee` lookup value for the project record. If no employee matches, ask the user which employee to use.

### Step 3: Decide create vs. update

- **Create**: no existing record for this project → `INSERT`.
- **Update**: the project already exists → `UPDATE` by `tu_employeeprofileprojectid`.

To find an existing project:

```sql
SELECT tu_employeeprofileprojectid, tu_name, tu_sortdate
FROM tu_employeeprofileproject
WHERE tu_employee = '<employeeid>'
ORDER BY tu_sortdate DESC
```

### Step 4a: Create (INSERT)

Always write German and English fields:

```sql
INSERT INTO tu_employeeprofileproject
  (tu_name, tu_name_en, tu_projectperiod, tu_projectperiod_en,
   tu_industry, tu_industry_en, tu_role, tu_role_en,
   tu_tasksandactivities, tu_tasksandactivities_en,
   tu_toolsandmethodsused, tu_toolsandmethodsused_en,
   tu_sortdate, tu_employee)
VALUES
  ('<Name>', '<Name_en>', '<Projektzeitraum>', '<Projektzeitraum_en>',
   '<Branche>', '<Branche_en>', '<Rolle>', '<Rolle_en>',
   '<Aufgaben / Tätigkeiten>', '<Aufgaben / Tätigkeiten_en>',
   '<Werkzeuge / Methoden>', '<Werkzeuge / Methoden_en>',
   '<Sort Datum>', '<employeeid>')
```

### Step 4b: Update (UPDATE)

Always include a `WHERE` on the primary key — `UPDATE` without `WHERE` is rejected by the server. Update both German and English fields:

```sql
UPDATE tu_employeeprofileproject
SET tu_name = '<Name>',
    tu_name_en = '<Name_en>',
    tu_projectperiod = '<Projektzeitraum>',
    tu_projectperiod_en = '<Projektzeitraum_en>',
    tu_industry = '<Branche>',
    tu_industry_en = '<Branche_en>',
    tu_role = '<Rolle>',
    tu_role_en = '<Rolle_en>',
    tu_tasksandactivities = '<Aufgaben / Tätigkeiten>',
    tu_tasksandactivities_en = '<Aufgaben / Tätigkeiten_en>',
    tu_toolsandmethodsused = '<Werkzeuge / Methoden>',
    tu_toolsandmethodsused_en = '<Werkzeuge / Methoden_en>',
    tu_sortdate = '<Sort Datum>'
WHERE tu_employeeprofileprojectid = '<guid>'
```

### Step 5: Approval gate

The server enforces a two-phase write gate when `DATAVERSE_APPROVAL_GATE=on`:

1. `ExecuteSQL` with `INSERT`/`UPDATE` returns a **preview + confirm token** (nothing is written yet).
2. Only `mcp-dataverse_ConfirmWrite(token)` executes the write. Tokens are single-use and expire after 5 minutes.

If the gate is `off` (current project config), writes execute immediately — but still confirm the intended change with the user before writing, and never write without explicit user intent.

`DELETE` is always rejected. `UPDATE` without `WHERE` is always rejected. Multi-statement batches are always rejected.

## Field Content Rules

- **German AND English:** every field must be written in German and English (`_en`). Never store only one language.
- **ASCII-safe (no umlauts):** all text values must use `ae`/`oe`/`ue`/`ss` instead of `ä`/`ö`/`ü`/`ß` (e.g. `Taetigkeiten` instead of `Tätigkeiten`, `fuer` instead of `für`). This applies to the tasks description and every other text field.
- `tu_tasksandactivities`: one compact sentence or semicolon-separated list (4–8 grouped activities).
- `tu_toolsandmethodsused`: concise comma-separated stack line with exact technology names (e.g. `C#, .NET 10, ASP.NET Core, React, TypeScript, SignalR, Azure OpenAI`).
- `tu_sortdate`: first day of the project end month, ISO `YYYY-MM-DD` (e.g. `2026-09-01`).
- `tu_projectperiod`: `MM.YYYY - MM.YYYY` (e.g. `08.2026 - 09.2026`).
- Do not invent customer names, certifications, production volumes, or business results without evidence.

## Quality Check

- [ ] Employee resolved to a valid `tu_employeeid`
- [ ] `tu_employee` lookup set on the project record
- [ ] Every field written in German AND English (`_en`)
- [ ] All text values ASCII-safe — no umlauts
- [ ] `tu_sortdate` is the first day of the end month
- [ ] `tu_projectperiod` is consistently formatted
- [ ] UPDATE has a `WHERE` on the primary key
- [ ] Write confirmed (gate) or explicitly intended by the user (gate off)
- [ ] Verify after write: re-query the record and confirm the values landed