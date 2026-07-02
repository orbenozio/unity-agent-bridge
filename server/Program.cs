// Program.cs - entry point. Two front-ends over the same Unity bridge:
//   - NO args  -> MCP stdio server (launched by Claude Code).
//   - WITH args -> CLI: run a single tool call and exit (see Cli.cs).
//
// IMPORTANT: this process is a DUMB, FAST PIPE. No Unity logic here.
// NOTE: stdout is reserved for the MCP transport. Log to STDERR only.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnityAgentBridge;

// --- CLI mode -----------------------------------------------------------------
if (args.Length > 0)
    return await Cli.RunAsync(args);

// --- MCP stdio server mode ----------------------------------------------------
var builder = Host.CreateApplicationBuilder(args);

// stdout belongs to the MCP transport - route all logging to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Port precedence: $UNITY_BRIDGE_PORT > $UNITY_BRIDGE_PROJECT discovery > default. The
// project form lets a client follow one Editor's port across restarts when several run.
int port = UnityClient.DefaultPort;
if (int.TryParse(Environment.GetEnvironmentVariable("UNITY_BRIDGE_PORT"), out var envPort))
{
    port = envPort;
}
else if (Environment.GetEnvironmentVariable("UNITY_BRIDGE_PROJECT") is { Length: > 0 } proj)
{
    var matches = BridgeDiscovery.Match(proj);
    if (matches.Count == 1) port = matches[0].Port;
    else Console.Error.WriteLine($"[unity-agent-bridge] UNITY_BRIDGE_PROJECT='{proj}' matched {matches.Count} bridges; using port {port}.");
}
builder.Services.AddSingleton(_ => new UnityClient(port));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(); // discovers UnityMcpTools ([McpServerToolType])

await builder.Build().RunAsync();
return 0;
