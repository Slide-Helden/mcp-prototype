using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TraceServer;

[McpServerToolType]
public static class TraceTools
{
    [McpServerTool(Name = "trace.echo")]
    [Description("Einfache Echo-Antwort, praktisch fuer Test-Calls. Schreibt sich in die Trace-Log.")]
    public static object Echo(
        [Description("Nachricht, die zurückgegeben wird.")] string message) =>
        new
        {
            echo = message,
            info = "Call per MCP sichtbar: /trace/logs oder Resource trace.logs lesen."
        };

    [McpServerTool(Name = "trace.ping")]
    [Description("Kurzer PING, um die Verbindung zu testen.")]
    public static object Ping() => new { pong = DateTimeOffset.UtcNow };
}
