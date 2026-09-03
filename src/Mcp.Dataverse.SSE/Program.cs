using Mcp.Dataverse.Core.Prompts;
using Mcp.Dataverse.Core.Tools;
using Mcp.Dataverse.Core.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port))
{
    builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
}

builder.AddDataverse();
builder.Services.AddMemoryCache();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.AddMcpServer()
    .WithPrompts<QueryPrompts>()
    .WithPrompts<DataversePrompts>()
    .WithHttpTransport()
    .WithTools<DataverseTool>();

var app = builder.Build();
app.UseCors();
app.MapGet("/", () => new { status = "running", service = "Dataverse MCP" });
app.MapGet("/health", () => new { status = "ok" });
app.MapMcp("/api/mcp");
app.Run();