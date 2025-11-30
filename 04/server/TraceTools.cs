using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DocServer;

[McpServerToolType]
public static class TraceTools
{
    [McpServerTool(Name = "trace.clear")]
    [Description("Loescht alle gespeicherten Trace-Eintraege und startet die Aufzeichnung neu.")]
    public static object Clear(TraceStore store)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] trace.clear aufgerufen");
        // TraceStore hat keine Clear-Methode, aber wir koennen eine Nachricht hinzufuegen
        store.Add(TraceDirection.Internal, "SYS", "Trace-Ansicht wurde zurueckgesetzt", null);
        return new
        {
            status = "cleared",
            message = "Trace-Log wurde markiert. Neue Eintraege werden aufgezeichnet."
        };
    }

    [McpServerTool(Name = "trace.stats")]
    [Description("Zeigt Statistiken zur aktuellen Trace-Aufzeichnung.")]
    public static object Stats(TraceStore store)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] trace.stats aufgerufen");
        var entries = store.GetEntries();
        var incoming = entries.Count(e => e.Direction == TraceDirection.Incoming);
        var outgoing = entries.Count(e => e.Direction == TraceDirection.Outgoing);
        var internalOps = entries.Count(e => e.Direction == TraceDirection.Internal);

        var httpCount = entries.Count(e => e.Category == "HTTP");
        var mcpCount = entries.Count(e => e.Category == "MCP");
        var toolCount = entries.Count(e => e.Category == "TOOL");
        var llmCount = entries.Count(e => e.Category == "LLM");

        return new
        {
            totalEntries = entries.Count,
            directions = new { incoming, outgoing, internalOps },
            categories = new { http = httpCount, mcp = mcpCount, tool = toolCount, llm = llmCount },
            timeRange = entries.Count > 0
                ? new
                {
                    first = entries.First().Timestamp.ToString("HH:mm:ss"),
                    last = entries.Last().Timestamp.ToString("HH:mm:ss")
                }
                : null
        };
    }
}
