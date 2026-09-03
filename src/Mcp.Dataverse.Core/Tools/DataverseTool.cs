using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MarkMpn.Sql4Cds.Engine;
using Mcp.Dataverse.Core.Extensions;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Mcp.Dataverse.Core.Tools;

[McpServerToolType]
public sealed class DataverseTool
{
    private static readonly TimeSpan _defaultCachingDuration = TimeSpan.FromMinutes(2);

    private const string ConnectHint = " Requires a Dataverse connection - if you have not connected yet in this session, call Connect first.";

    [McpServerTool, Description("Establishes the Dataverse connection (interactive browser login on first use, afterwards silent via token cache). Call this once before using any other Dataverse tool in a session.")]
    public static async Task<string> Connect(
        Sql4CdsConnection sql4cdsConnection)
    {
        // cheap real roundtrip: warms connection + token cache, proves auth works
        return await ExecuteSelect("SELECT TOP(1) logicalname FROM metadata.entity", sql4cdsConnection);
    }

    [McpServerTool, Description("Get metadata for all tables in Dataverse." + ConnectHint)]
    public static async Task<string> GetMetadataForAllTables(
        Sql4CdsConnection sql4cdsConnection,
        IMemoryCache cache,
        [Description(@"The metadata columns to retrieve e.g. [""metadataid"", ""logicalname""")] string[] metadataFieldNames,
        [Description("Condition to filter down the table metadata e.g. isactivity = 1 AND islogicalentity = 1")] string? conditions)
    {
        var cacheKey = $"GetMetadataForAllTables_{string.Join(",", metadataFieldNames)}_{conditions}";
        if (cache.TryGetValue(cacheKey, out string? cachedResult)) return cachedResult!;

        var query = metadataFieldNames.Length > 0 ? $"SELECT {string.Join(",", metadataFieldNames)} FROM metadata.entity" : $"SELECT * FROM metadata.entity";
        if (!string.IsNullOrEmpty(conditions))
        {
            query += $" WHERE ({conditions.ToLower()})";
        }
        var result = await ExecuteSelect(query, sql4cdsConnection);
        cache.Set(cacheKey, result, _defaultCachingDuration);
        return result;
    }

    [McpServerTool, Description("Get metadata for a specific table." + ConnectHint)]
    public static async Task<string> GetMetadataByTableName(
        Sql4CdsConnection sql4cdsConnection,
        IMemoryCache cache,
        [Description("The table's logical name e.g. contact, account")] string tableName,
        [Description(@"The metadata columns to retrieve e.g. [""metadataid"", ""logicalname""")] string[] metadataFieldNames)
    {
        var cacheKey = $"GetMetadataByTableName_{tableName}_{string.Join(",", metadataFieldNames)}";
        if (cache.TryGetValue(cacheKey, out string? cachedResult)) return cachedResult!;

        var query = metadataFieldNames.Length > 0 ? $"SELECT {string.Join(",", metadataFieldNames)} FROM metadata.entity" : $"SELECT * FROM metadata.entity";
        var result = await ExecuteSelect($"{query} WHERE logicalname = '{tableName}'", sql4cdsConnection);
        cache.Set(cacheKey, result, _defaultCachingDuration);
        return result;
    }

    [McpServerTool, Description("Get metadata for fields in a specific table." + ConnectHint)]
    public static async Task<string> GetFieldMetadataByTableName(
        Sql4CdsConnection sql4cdsConnection,
        IMemoryCache cache,
        [Description("The table's logical name e.g. contact, account")] string tableName,
        [Description(@"The metadata columns to retrieve e.g. [""metadataid"", ""isvalidforread""")] string[] metadataFieldNames,
        [Description("Condition to filter down the attribute metadata e.g. isfilterable = 1 AND isvalidforupdate = 1")] string? conditions)
    {
        var cacheKey = $"GetFieldMetadataByTableName_{tableName}_{string.Join(",", metadataFieldNames)}_{conditions}";
        if (cache.TryGetValue(cacheKey, out string? cachedResult)) return cachedResult!;

        var query = metadataFieldNames.Length > 0 ? $"SELECT {string.Join(",", metadataFieldNames)} FROM metadata.attribute" : $"SELECT * FROM metadata.attribute";
        query += $" WHERE attribute.entitylogicalname = '{tableName}'";
        if (!string.IsNullOrEmpty(conditions))
        {
            query += $" AND ({conditions.ToLower()})";
        }
        var result = await ExecuteSelect(query, sql4cdsConnection);
        cache.Set(cacheKey, result, _defaultCachingDuration);
        return result;
    }

    [McpServerTool, Description("Retrieve rows for a specific table." + ConnectHint)]
    public static async Task<string> GetRowsForTable(
        Sql4CdsConnection sql4cdsConnection,
        [Description("The table's logical name e.g. contact, account")] string tableName,
        [Description(@"The field names to retrieve from the table e.g. [""contactid"", ""fullname""")] string[] fieldNames,
        [Description("Condition to filter down the table")] string? conditions,
        [Description("The sort order for the results e.g. fullname DESC.)")] string? sortOrder,
        [Description("The number of rows to retrieve. Defaults to 50.")] int? rowCount = 50)
    {
        var query = fieldNames.Length > 0 ? $"SELECT TOP({rowCount}) {string.Join(",", fieldNames)} FROM dbo.{tableName}" : $"SELECT TOP({rowCount}) * FROM dbo.{tableName}";
        if (!string.IsNullOrEmpty(conditions))
        {
            query += $" WHERE ({conditions})";
        }
        if (!string.IsNullOrEmpty(sortOrder))
        {
            query += $" ORDER BY {sortOrder}";
        }
        var result = await ExecuteSelect(query, sql4cdsConnection, tableName.ToLowerInvariant());
        return result;
    }

    [McpServerTool, Description("Executes an SQL query against Dataverse. SELECT returns results directly. INSERT/UPDATE return a preview plus a confirm token - the write only executes after ConfirmWrite(token). DELETE is not allowed." + ConnectHint)]
    public static async Task<string> ExecuteSQL(
        Sql4CdsConnection sql4cdsConnection,
        DataverseGateOptions gateOptions,
        [Description("A single SQL statement: SELECT to read, or INSERT/UPDATE to write (write requires confirmation via ConfirmWrite). Multiple statements are not allowed.")] string sqlQuery,
        [Description("Bypasses registered plugin steps and real-time workflows during INSERT/UPDATE. Requires system administrator privileges. Ask the user before setting this to true.")] bool bypassCustomPlugins = false)
    {
        var statement = sqlQuery.TrimStart();
        // single pass: literals + comments masked -> all keyword/statement analysis runs on safe text
        var masked = MaskLiteralsAndComments(sqlQuery);
        var words = SqlKeywords(masked);
        var first = words.FirstOrDefault()?.ToUpperInvariant() ?? "";

        if (first is not "SELECT" and not "WITH" and not "INSERT" and not "UPDATE")
        {
            throw new McpException("Only SELECT, INSERT or UPDATE statements are allowed. DELETE and other statement types are rejected.");
        }
        if (first == "WITH" && words.Any(w => w is "UPDATE" or "INSERT" or "DELETE"))
        {
            throw new McpException("DML inside WITH/CTE statements is not supported. Use a plain INSERT or UPDATE statement.");
        }
        if (!IsSingleStatement(masked))
        {
            throw new McpException("Only a single SQL statement is allowed (no statement batches separated by ';').");
        }
        if (first is "SELECT" or "WITH")
        {
            // record links only for plain single-table SELECTs - joins/unions have ambiguous table+id pairs
            string? linkTable = null;
            if (first == "SELECT" && !words.Any(w => w is "JOIN" or "UNION"))
            {
                var fromMatch = Regex.Match(masked, @"\bFROM\s+\[?([A-Za-z0-9_]+)\]?", RegexOptions.IgnoreCase);
                if (fromMatch.Success) linkTable = fromMatch.Groups[1].Value.ToLowerInvariant();
            }
            return await ExecuteSelect(sqlQuery, sql4cdsConnection, linkTable);
        }
        if (!gateOptions.RequireApproval)
        {
            // gate disabled via DATAVERSE_APPROVAL_GATE=off: execute the write directly
            var affectedDirect = await ExecuteNonQuery(statement, sql4cdsConnection, bypassCustomPlugins);
            var directLinks = await WriteRecordLinks(statement, sql4cdsConnection);
            return $"""
            <write_executed>
                <statement>{statement}</statement>
                <affected_rows>{affectedDirect}</affected_rows>
                <bypass_custom_plugins>{(bypassCustomPlugins ? "yes" : "no")}</bypass_custom_plugins>
            </write_executed>
            {directLinks}
            """;
        }
        return await CreateWritePreview(sqlQuery, words, sql4cdsConnection, bypassCustomPlugins);
    }

    [McpServerTool, Description("Executes a previously previewed INSERT/UPDATE statement. Pass the confirm token returned by ExecuteSQL. Tokens are valid for 5 minutes. Only call this after the user explicitly approved the preview.")]
    public static async Task<string> ConfirmWrite(
        Sql4CdsConnection sql4cdsConnection,
        [Description("The confirm token from the ExecuteSQL preview.")] string token)
    {
        if (!_pendingWrites.TryRemove(token, out var pending) || pending.IsExpired)
        {
            throw new McpException("Invalid or expired confirm token. Run ExecuteSQL again to get a new preview.");
        }
        var affected = await ExecuteNonQuery(pending.Sql, sql4cdsConnection, pending.BypassCustomPlugins);
        var recordLinks = await WriteRecordLinks(pending.Sql, sql4cdsConnection);
        return $"""
        <write_executed>
            <statement>{pending.Sql}</statement>
            <affected_rows>{affected}</affected_rows>
            <bypass_custom_plugins>{(pending.BypassCustomPlugins ? "yes" : "no")}</bypass_custom_plugins>
        </write_executed>
        {recordLinks}
        """;
    }

    private sealed record PendingWrite(string Sql, DateTime ExpiresAt, bool BypassCustomPlugins)
    {
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    private static readonly ConcurrentDictionary<string, PendingWrite> _pendingWrites = new();
    private static readonly TimeSpan _pendingWriteTtl = TimeSpan.FromMinutes(5);

    private static async Task<string> CreateWritePreview(string sql, string[] maskedWords, Sql4CdsConnection sql4cdsConnection, bool bypassCustomPlugins)
    {
        var statement = sql.TrimStart();
        var isUpdate = statement.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase);

        if (isUpdate && !maskedWords.Contains("WHERE", StringComparer.OrdinalIgnoreCase))
        {
            throw new McpException("UPDATE without WHERE would modify every row in the table and is rejected. Add a WHERE condition.");
        }

        var tableMatch = Regex.Match(statement, @"(?:INSERT\s+INTO|UPDATE)\s+\[?([A-Za-z0-9_]+)\]?", RegexOptions.IgnoreCase);
        var table = tableMatch.Success ? tableMatch.Groups[1].Value : "(unknown)";

        var estimate = await TryBuildRowCountEstimate(statement, isUpdate, sql4cdsConnection);

        // expire stale pending writes, then register the new one
        foreach (var expired in _pendingWrites.Where(kv => kv.Value.IsExpired).Select(kv => kv.Key).ToArray())
        {
            _pendingWrites.TryRemove(expired, out _);
        }
        var token = Guid.NewGuid().ToString("N");
        _pendingWrites[token] = new PendingWrite(statement, DateTime.UtcNow.Add(_pendingWriteTtl), bypassCustomPlugins);

        return $"""
        <write_preview>
            <target_table>{table}</target_table>
            <estimated_rows>{estimate}</estimated_rows>
            <bypass_custom_plugins>{(bypassCustomPlugins ? "yes" : "no")}</bypass_custom_plugins>
            <statement>{statement}</statement>
            <confirm_token>{token}</confirm_token>
        </write_preview>
        Nothing has been written yet. Show this preview to the user and ask for approval. Only after explicit user approval call ConfirmWrite with the confirm_token.
        """;
    }

    private static async Task<string> TryBuildRowCountEstimate(string statement, bool isUpdate, Sql4CdsConnection sql4cdsConnection)
    {
        if (!isUpdate) return "n/a (insert)";
        var match = Regex.Match(statement, @"^UPDATE\s+\[?([A-Za-z0-9_]+)\]?\s+SET\s+.*\s+WHERE\s+(?<pred>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return "n/a (complex statement)";
        try
        {
            using var cmd = sql4cdsConnection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {match.Groups[1].Value} WHERE {match.Groups["pred"].Value}";
            var count = await cmd.ExecuteScalarAsync();
            return count?.ToString() ?? "n/a";
        }
        catch
        {
            return "n/a (estimate failed)";
        }
    }

    // masks string literals ('' escaping) and -- / /* */ comments with spaces, preserving character
    // positions, so keyword and ';' analysis never looks inside literals or comments
    private static string MaskLiteralsAndComments(string sql)
    {
        var masked = new char[sql.Length];
        var i = 0;
        while (i < sql.Length)
        {
            var ch = sql[i];
            if (ch == '\'' || ch == '"')
            {
                masked[i++] = ' ';
                while (i < sql.Length)
                {
                    if (sql[i] == ch)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == ch) { masked[i++] = ' '; masked[i++] = ' '; continue; } // doubled quote = escaped literal char
                        masked[i++] = ' ';
                        break;
                    }
                    masked[i++] = ' ';
                }
                continue;
            }
            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') masked[i++] = ' ';
                continue;
            }
            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                masked[i++] = ' '; masked[i++] = ' ';
                while (i < sql.Length)
                {
                    if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/') { masked[i++] = ' '; masked[i++] = ' '; break; }
                    masked[i++] = ' ';
                }
                continue;
            }
            masked[i++] = ch;
        }
        return new string(masked);
    }

    private static string[] SqlKeywords(string maskedSql)
    {
        var sb = new System.Text.StringBuilder(maskedSql.Length);
        foreach (var ch in maskedSql)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '@' || ch == '#' ? ch : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsSingleStatement(string maskedSql)
    {
        var trimmed = maskedSql.TrimEnd();
        if (trimmed.EndsWith(";")) trimmed = trimmed[..^1].TrimEnd(); // a single trailing terminator is fine
        return !trimmed.Contains(';');
    }

    [McpServerTool, Description("Convert FetchXml query to SQL query." + ConnectHint)]
    public static async Task<string> ConvertFetchXmlToSql(
        Sql4CdsConnection sql4cdsConnection,
        [Description("FetchXml query")] string fetchXml)
    {
        var result = await ExecuteSelect($"SELECT Response FROM FetchXMLToSQL('{fetchXml}',0)", sql4cdsConnection);
        return result;
    }
    private static async Task<string> ExecuteSelect(string query, Sql4CdsConnection sql4cdsConnection, string? linkTable = null)
    {
        using Sql4CdsCommand cmd = sql4cdsConnection.CreateCommand();
        cmd.CommandText = query;
        var table = new List<Dictionary<string, object>>();
        try
        {
            var reader = await cmd.ExecuteReaderAsync();
            int rowCount = 1;
            while (await reader.ReadAsync())
            {
                var rows = new Dictionary<string, object>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (i == 0)
                        rows["#"] = rowCount++;
                    rows[reader.GetName(i) ?? $"column_{i + 1}"] = reader.GetValue(i);
                }
                table.Add(rows);
            }
            var result = JsonSerializer.Serialize(table, options: new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var output = $"""
            <environment>
                https://{sql4cdsConnection.DataSource}
            </environment>
            <json_output>
                {result}
            </json_output>
            """;
            return linkTable == null ? output : AppendRecordLinks(output, linkTable, sql4cdsConnection, table);
        }
        catch (Sql4CdsException ex)
        {
            return $"""
            <error>
                {ex.Message}
            </error>
            """;
        }
        catch (Exception ex)
        {
            return $"""
            <error>
                {ex.Message}
            </error>
            """;
        }
    }

    private static async Task<int> ExecuteNonQuery(string query, Sql4CdsConnection sql4cdsConnection, bool bypassCustomPlugins = false)
    {
        using Sql4CdsCommand cmd = sql4cdsConnection.CreateCommand();
        cmd.CommandText = query;
        var previous = sql4cdsConnection.BypassCustomPlugins;
        sql4cdsConnection.BypassCustomPlugins = bypassCustomPlugins;
        try
        {
            return await cmd.ExecuteNonQueryAsync();
        }
        catch (Sql4CdsException ex)
        {
            throw new McpException($"Write failed: {ex.Message}");
        }
        finally
        {
            sql4cdsConnection.BypassCustomPlugins = previous;
        }
    }

    private const int MaxRecordLinks = 20;

    private static string AppendRecordLinks(string output, string linkTable, Sql4CdsConnection sql4cdsConnection, List<Dictionary<string, object>> rows)
    {
        var idColumn = linkTable + "id";
        var links = new List<string>();
        foreach (var row in rows)
        {
            if (links.Count >= MaxRecordLinks) break;
            if (row.TryGetValue(idColumn, out var value) && value is Guid id)
                links.Add($"https://{sql4cdsConnection.DataSource}/main.aspx?pagetype=entityrecord&etn={linkTable}&id={id}");
        }
        return links.Count == 0
            ? output
            : output + "\n<record_links>\n" + string.Join("\n", links) + "\n</record_links>";
    }

    // after an UPDATE, query back the affected rows and emit their record links;
    // INSERT returns no server-generated id here, so no links - the LLM can run a follow-up SELECT
    private static async Task<string> WriteRecordLinks(string statement, Sql4CdsConnection sql4cdsConnection)
    {
        if (!statement.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) return "";
        var match = Regex.Match(statement, @"^UPDATE\s+\[?([A-Za-z0-9_]+)\]?\s+SET\s+.*\s+WHERE\s+(?<pred>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return "";
        var table = match.Groups[1].Value;
        try
        {
            return await ExecuteSelect($"SELECT {table}id FROM {table} WHERE {match.Groups["pred"].Value}", sql4cdsConnection, table);
        }
        catch (McpException)
        {
            return "";
        }
    }
}