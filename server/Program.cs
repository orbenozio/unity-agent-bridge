// Program.cs — MCP server entry point (stdio transport).
//
// Responsibilities (see SPEC.md §7, MILESTONES M1):
//   - Build a generic host.
//   - Register the stdio MCP server and discover [McpServerTool] methods.
//   - Register UnityClient as a singleton (the WebSocket pipe to the Unity bridge).
//
// IMPORTANT: this process is a DUMB, FAST PIPE. No Unity logic here.
// NOTE: stdout is reserved for the MCP stdio transport. Log to STDERR only.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnityMcpBridge;

var builder = Host.CreateApplicationBuilder(args);

// stdout belongs to the MCP transport — route all logging to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<UnityClient>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(); // discovers UnityMcpTools ([McpServerToolType])

await builder.Build().RunAsync();
