---
name: dataverse-query
description: Query Microsoft Dataverse via the mcp-dataverse MCP server - metadata discovery, SELECT queries, FetchXML conversion. Use when the user wants to read or explore Dataverse data.
---

# Dataverse Query

Read data from Microsoft Dataverse using the `mcp-dataverse` MCP server.

## Workflow: connect, metadata, query

0. **Connect first**: call `Connect` once at the start of a session. It opens the
   interactive login (browser) on first use and is silent afterwards (token cache).
   Never call other Dataverse tools while the connection is unauthenticated - they
   would each block on a login prompt.

1. **Discover tables**: `GetMetadataForAllTables` with a small field list
   (`["metadataid", "logicalname"]`) and conditions to filter, e.g. `isactivity = 1`.
2. **Inspect table**: `GetMetadataByTableName` with `tableName` (logical name, e.g. `contact`).
3. **Inspect fields**: `GetFieldMetadataByTableName` — check `isvalidforread` before querying a field.
4. **Read rows**: `GetRowsForTable` (fields, filter, sort, row count) or free-form SQL via `ExecuteSQL`.
   Single-table queries return `<record_links>` - clickable Dataverse URLs to the records; share them with the user.

## SQL rules

- Dialect: T-SQL-like, translated to Dataverse FetchXML by Sql4Cds.
- Logical collection names work: `SELECT fullname, emailaddress1 FROM contact WHERE ...`
- Metadata is queryable as pseudo-tables: `metadata.entity`, `metadata.attribute`.
- Row limits: always use `TOP(n)` on exploratory queries.
- `ExecuteSQL` accepts **SELECT** here. For writes, see the `dataverse-write` skill (and its
  field selection workflow - fetch field metadata and resolve ambiguous fields with the user
  before writing).

## FetchXML

Have an existing FetchXML query? Convert with `ConvertFetchXmlToSql`, then refine the SQL.

## Tips

- Filter on indexed fields for large tables.
- `createdon`, `modifiedon` are UTC (`UseLocalTimeZone` is on, output reflects local time).
- If a query fails with a column error, re-check field metadata — schema names are case-sensitive.
