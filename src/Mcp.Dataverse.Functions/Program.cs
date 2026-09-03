using Mcp.Dataverse.Core.Extensions;
using Mcp.Dataverse.Core.Prompts;
using Mcp.Dataverse.Core.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

builder.AddDataverse();
builder.Services.AddMemoryCache();
builder.Services.AddMcpServer()
    .WithPrompts<QueryPrompts>()
    .WithPrompts<DataversePrompts>()
    .WithHttpTransport()
    .WithTools<DataverseTool>();

var app = builder.Build();
app.MapGet("/", () => new { status = "running", service = "Dataverse MCP" });
app.MapGet("/api/health", () => new { status = "ok" });
app.MapMcp("/mcp");
await app.RunAsync();
