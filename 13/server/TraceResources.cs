using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TraceServer;

[McpServerResourceType]
public static class TraceResources
{
    [McpServerResource(
        Name = "trace.logs",
        Title = "Trace Log",
        MimeType = "text/plain",
        UriTemplate = "trace/logs")]
    [Description("Gibt die letzten aufgezeichneten HTTP-Anfragen/Antworten aus.")]
    public static string Logs(TraceStore store) => store.Dump();
}
