using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DocServer;

[McpServerResourceType]
public static class TraceResources
{
    [McpServerResource(
        Name = "trace.logs",
        Title = "Communication Trace Log",
        MimeType = "text/plain",
        UriTemplate = "trace/logs")]
    [Description("Gibt die aufgezeichneten MCP-Kommunikations-Traces aus (HTTP, JSON-RPC, Tool-Calls).")]
    public static string Logs(TraceStore store) => store.Dump();

    [McpServerResource(
        Name = "trace.logs.markdown",
        Title = "Communication Trace (Markdown)",
        MimeType = "text/markdown",
        UriTemplate = "trace/logs/markdown")]
    [Description("Gibt die MCP-Traces als Markdown-Tabelle aus.")]
    public static string LogsMarkdown(TraceStore store) => store.DumpMarkdown();
}
