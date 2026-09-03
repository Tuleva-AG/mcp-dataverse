using Mcp.Dataverse.Core.Prompts;
using Mcp.Dataverse.Core.Tools;
using Mcp.Dataverse.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.None);

builder.AddDataverse();
builder.Services.AddMemoryCache();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithPrompts<QueryPrompts>()
    .WithPrompts<DataversePrompts>()
    .WithTools<DataverseTool>();
await builder.Build().RunAsync();