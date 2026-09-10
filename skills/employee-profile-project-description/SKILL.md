---
name: employee-profile-project-description
description: Create a polished project description for an employee profile, CV, project list, or recruiter submission. Produces a JSON object plus three text variants: concise profile text, technical CV text, and business-oriented text.
---

# Employee Profile Project Description

Create a professional German-language project description from repository context, user notes, or manually supplied facts.

## When to Use

- The user wants a project description for a Mitarbeiterprofil, CV, Projektliste, LinkedIn, Xing, or recruiter profile
- The user provides a repository and wants role, tasks, technologies, and timeframe condensed into profile-ready wording
- The user wants the output in a specific structured JSON format
- The user wants multiple wording variants for different audiences

## When Not to Use

- The user wants a README, product documentation, or technical architecture document
- The user wants implementation help or code changes instead of profile text
- The user needs a project pitch deck or sales proposal rather than a personal project reference

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project facts or repository context | Yes | Project purpose, technologies, architecture, and responsibilities |
| Target audience | No | CV, Mitarbeiterprofil, LinkedIn/Xing, recruiter, sales, etc. |
| Desired style | No | concise, technical, business-oriented, or mixed |
| Desired format | No | JSON only, text only, or both |

## Output Contract

Unless the user requests otherwise, produce all of the following:

1. A JSON object with these keys (German and English):
   - `Name` / `Name_en`
   - `Projektzeitraum` / `Projektzeitraum_en`
   - `Branche` / `Branche_en`
   - `Rolle` / `Rolle_en`
   - `Aufgaben / Taetigkeiten` / `Aufgaben / Taetigkeiten_en`
   - `Werkzeuge / Methoden` / `Werkzeuge / Methoden_en`
   - `Sort Datum`
2. `Kurzvariante` (DE) + `Kurzvariante_en` (EN) for profile portals
3. `Fachliche Variante` (DE) + `Fachliche Variante_en` (EN) for CV/project lists
4. `Business-Variante` (DE) + `Business-Variante_en` (EN) for recruiters or account managers

**ASCII rule (mandatory):** All JSON field values and text variants must use ASCII-safe spelling — no umlauts or accented characters. Use `ae`, `oe`, `ue`, `ss` instead of `ä`, `ö`, `ü`, `ß` (e.g. `Taetigkeiten` instead of `Tätigkeiten`, `fuer` instead of `für`). This applies to the `Aufgaben / Taetigkeiten` description and every other field. The profile projects are stored in Dataverse, which requires plain ASCII.

## Workflow

### Step 1: Gather project facts

Use available context to extract the following:

- Project objective and business purpose
- Timeframe
- Industry or business domain
- User role
- Main responsibilities
- Key technologies and methods
- Notable integrations, architecture patterns, or delivery responsibilities

Preferred evidence sources:

1. User-provided facts
2. Repository instructions such as `AGENTS.md`, `.github/copilot-instructions.md`, `README.md`, solution layout, and project files
3. Git history only when timeframe or evolution must be inferred

If an exact timeframe is unknown, infer a reasonable range from the repository and make that clear in wording if needed.

### Step 2: Normalize the content

Condense the findings into profile-friendly language:

- Focus on contribution and responsibility, not commit-by-commit detail
- Group related tasks into 4-8 meaningful activities
- Group technologies into a compact, recruiter-friendly stack line
- Prefer role labels such as `Developer`, `Full-Stack Developer`, `Architekt`, `Tech Lead`, or combinations that fit the evidence
- Keep the wording concrete and professional, not marketing-heavy

### Step 3: Build the JSON object

Create a JSON object in this structure (German and English):

```json
{
  "Name": "...",
  "Name_en": "...",
  "Projektzeitraum": "MM.YYYY - MM.YYYY",
  "Projektzeitraum_en": "MM.YYYY - MM.YYYY",
  "Branche": "...",
  "Branche_en": "...",
  "Rolle": "...",
  "Rolle_en": "...",
  "Aufgaben / Taetigkeiten": "...",
  "Aufgaben / Taetigkeiten_en": "...",
  "Werkzeuge / Methoden": "...",
  "Werkzeuge / Methoden_en": "...",
  "Sort Datum": "YYYY-MM-01"
}
```

Rules:

- `Name` should be descriptive but not overloaded
- `Projektzeitraum` should use `MM.YYYY - MM.YYYY`
- `Sort Datum` should normally be the first day of the project end month
- `Aufgaben / Taetigkeiten` should be one compact sentence or semicolon-separated list
- `Werkzeuge / Methoden` should be a concise comma-separated list
- **Every field must be provided in German AND English** (`_en` suffix for English)
- **All values must be ASCII-safe** — no umlauts (`ae`/`oe`/`ue`/`ss` instead of `ä`/`ö`/`ü`/`ß`)

### Step 4: Create the text variants (German and English)

Produce each variant in German and English (`_en` suffix). All variants must be ASCII-safe (no umlauts).

#### Kurzvariante / Kurzvariante_en

- 2-4 short lines or one compact paragraph
- Suitable for Mitarbeiterprofile, Xing, or project overviews
- Focus on value, role, and core stack

#### Fachliche Variante / Fachliche Variante_en

- 1 compact paragraph
- More technical and architecture-oriented
- Suitable for CVs and detailed project lists

#### Business-Variante / Business-Variante_en

- 1 compact paragraph
- Emphasize business value, solution scope, integrations, and responsibilities
- Suitable for recruiters, account managers, and non-technical readers

### Step 5: Quality check

Verify that the result:

- sounds like a real project reference and not like autogenerated boilerplate
- matches the actual project scope and technologies
- does not invent customer names, certifications, production volumes, or business results without evidence
- avoids internal jargon that external readers would not understand
- is grammatically clean and easy to reuse in profiles

## Style Guidance

- Default language: German
- Tone: professional, concise, consultant-friendly
- Prefer nouns and action-oriented phrasing over buzzwords
- Avoid exaggerated claims such as `revolutionaer`, `weltklasse`, or `state of the art` unless the user explicitly wants marketing language
- Keep technology names exact: `ASP.NET Core`, `SignalR`, `React`, `Azure Communication Services`, `Azure OpenAI`, `Microsoft Graph`, etc.
- When the role spans architecture and implementation, prefer combined role labels such as `Architekt, Full-Stack Developer`

## Recommended Heuristics

### Naming patterns

- If the system centers on telephony, streaming, assistants, or orchestration, reflect that in `Name`
- Good examples:
  - `Echtzeit-Kommunikationsplattform fuer Call-Steuerung und KI-Assistenz`
  - `Media Adapter fuer ACS-basierte Telefonie- und Echtzeitprozesse`
  - `Monitoring- und Steuerungsloesung fuer Call- und Medienprozesse`

### Role selection

- `Developer` if the evidence is primarily implementation
- `Architekt, Developer` if architecture and implementation are both clearly present
- `Full-Stack Developer` if backend and frontend responsibilities are both significant
- `Architekt, Full-Stack Developer` if architecture, backend, frontend, and integration ownership are all visible

### Task compression

Convert raw technical evidence into profile language:

- `implemented REST controllers, SignalR hubs, and WebSocket handling`
  -> `Entwicklung von REST APIs sowie Echtzeitkommunikation mit SignalR und WebSockets`
- `integrated Azure OpenAI, Graph, and CallAutomation`
  -> `Integration von Azure OpenAI, Microsoft Graph und Azure Communication Services`
- `built pipelines, tests, and deployment`
  -> `Aufbau automatisierter Tests sowie Build- und Deployment-Prozesse`

## Example Output

```json
{
  "Name": "Media Adapter fuer Echtzeit-Telefonie und KI-gestuetzte Call-Steuerung",
  "Name_en": "Media Adapter for Real-Time Telephony and AI-driven Call Control",
  "Projektzeitraum": "12.2024 - 03.2026",
  "Projektzeitraum_en": "12.2024 - 03.2026",
  "Branche": "IT-Dienstleister / Telekommunikation",
  "Branche_en": "IT service provider / Telecommunications",
  "Rolle": "Architekt, Full-Stack Developer",
  "Rolle_en": "Architect, Full-Stack Developer",
  "Aufgaben / Taetigkeiten": "Konzeption und Entwicklung einer ASP.NET Core Anwendung zur Steuerung von Telefonie- und Medienprozessen; Entwicklung von REST APIs, SignalR- und WebSocket-Kommunikation; Integration von Azure Communication Services, Azure OpenAI und Microsoft Graph; Entwicklung eines React-Frontends fuer Call Control und Monitoring; Implementierung von Messaging, Logging, Tests sowie CI/CD- und Deployment-Prozessen",
  "Aufgaben / Taetigkeiten_en": "Design and development of an ASP.NET Core application for controlling telephony and media processes; development of REST APIs, SignalR and WebSocket communication; integration of Azure Communication Services, Azure OpenAI and Microsoft Graph; development of a React frontend for call control and monitoring; implementation of messaging, logging, tests as well as CI/CD and deployment processes",
  "Werkzeuge / Methoden": "C#, .NET 10, ASP.NET Core, React, TypeScript, SignalR, WebSockets, Azure Communication Services, Azure OpenAI, Microsoft Graph, WolverineFx, xUnit, Azure DevOps, WiX",
  "Werkzeuge / Methoden_en": "C#, .NET 10, ASP.NET Core, React, TypeScript, SignalR, WebSockets, Azure Communication Services, Azure OpenAI, Microsoft Graph, WolverineFx, xUnit, Azure DevOps, WiX",
  "Sort Datum": "2026-03-01"
}
```

## Final Response Pattern

When using this skill, return:

1. A short lead-in sentence
2. The JSON object in a fenced `json` block (German and English fields)
3. `Kurzvariante` + `Kurzvariante_en`
4. `Fachliche Variante` + `Fachliche Variante_en`
5. `Business-Variante` + `Business-Variante_en`
6. Optional offer to tailor the wording for a specific target audience

## Validation Checklist

- [ ] Timeframe is plausible and consistently formatted
- [ ] Role matches actual contribution level
- [ ] Tasks are compact and profile-ready
- [ ] Tool list is relevant and not overloaded
- [ ] JSON is valid
- [ ] Every field is provided in German AND English (`_en`)
- [ ] All values are ASCII-safe — no umlauts (`ae`/`oe`/`ue`/`ss`)
- [ ] All text variants are distinct in tone and target audience
