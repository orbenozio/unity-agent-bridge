// Program.cs — MCP server entry point (stdio transport).
//
// Responsibilities (see SPEC.md §7, MILESTONES M1):
//   - Build a generic host.
//   - Register the stdio MCP server and discover [McpServerTool] methods.
//   - Register UnityClient as a singleton (the WebSocket pipe to the Unity bridge).
//
// IMPORTANT: this process is a DUMB, FAST PIPE. No Unity logic here.
//
// TODO(M1): wire up the host below. Pseudocode shape:
//
//   var builder = Host.CreateApplicationBuilder(args);
//   builder.Services.AddSingleton<UnityClient>();        // ws://127.0.0.1:17890
//   builder.Services
//          .AddMcpServer()
//          .WithStdioServerTransport()
//          .WithToolsFromAssembly();                      // picks up UnityMcpTools
//   await builder.Build().RunAsync();
//
// NOTE: stdout is reserved for the MCP stdio transport. Log to STDERR only.

namespace UnityMcpBridge;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        // TODO(M1): replace with the host setup described above.
        await Console.Error.WriteLineAsync(
            "[unity-mcp-bridge] server scaffold — implement Program.cs per SPEC.md §7 (M1).");
    }
}
