using System.Collections.Concurrent;
using System.Text;

namespace DocServer;

/// <summary>
/// Thread-safe in-memory trace store for MCP communication logging.
/// Captures HTTP requests/responses and MCP JSON-RPC messages.
/// </summary>
public sealed class TraceStore
{
    private const int MaxEntries = 128;
    private readonly ConcurrentQueue<TraceEntry> _entries = new();
    private int _sequence = 0;

    public void Add(TraceDirection direction, string category, string message, string? details = null)
    {
        var entry = new TraceEntry(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            direction,
            category,
            message,
            details);

        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
    }

    public void AddRequest(string method, string path, string? body = null)
    {
        var bodyPreview = TruncateBody(body);
        Add(TraceDirection.Incoming, "HTTP",
            $"{method} {path}",
            bodyPreview);
    }

    public void AddResponse(int statusCode, string? contentType = null)
    {
        Add(TraceDirection.Outgoing, "HTTP",
            $"Status {statusCode}",
            contentType);
    }

    public void AddMcpRequest(string method, string? parameters = null)
    {
        Add(TraceDirection.Incoming, "MCP",
            $"method: {method}",
            TruncateBody(parameters));
    }

    public void AddMcpResponse(string method, string? result = null)
    {
        Add(TraceDirection.Outgoing, "MCP",
            $"result: {method}",
            TruncateBody(result));
    }

    public void AddLlmCall(string description, string? details = null)
    {
        Add(TraceDirection.Internal, "LLM",
            description,
            TruncateBody(details));
    }

    public void AddToolCall(string toolName, string? arguments = null)
    {
        Add(TraceDirection.Internal, "TOOL",
            $"call: {toolName}",
            TruncateBody(arguments));
    }

    public void AddToolResult(string toolName, string? result = null)
    {
        Add(TraceDirection.Internal, "TOOL",
            $"result: {toolName}",
            TruncateBody(result));
    }

    public IReadOnlyList<TraceEntry> GetEntries() => _entries.ToList();

    public string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                           MCP COMMUNICATION TRACE                              ║");
        sb.AppendLine("╠════════════════════════════════════════════════════════════════════════════════╣");

        foreach (var entry in _entries)
        {
            var arrow = entry.Direction switch
            {
                TraceDirection.Incoming => "──►",
                TraceDirection.Outgoing => "◄──",
                TraceDirection.Internal => "◆◆◆",
                _ => "   "
            };

            var category = entry.Category.PadRight(4);
            sb.AppendLine($"║ [{entry.Sequence:D4}] {entry.Timestamp:HH:mm:ss.fff} {arrow} [{category}] {entry.Message,-40}");

            if (!string.IsNullOrWhiteSpace(entry.Details))
            {
                var detailLines = entry.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in detailLines.Take(3))
                {
                    var truncated = line.Length > 70 ? line[..70] + "..." : line;
                    sb.AppendLine($"║        {truncated}");
                }
            }
        }

        if (_entries.IsEmpty)
        {
            sb.AppendLine("║ (no traces captured yet)                                                       ║");
        }

        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        return sb.ToString();
    }

    public string DumpMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MCP Communication Trace");
        sb.AppendLine();
        sb.AppendLine("| # | Zeit | Richtung | Kategorie | Nachricht |");
        sb.AppendLine("|---|------|----------|-----------|-----------|");

        foreach (var entry in _entries)
        {
            var arrow = entry.Direction switch
            {
                TraceDirection.Incoming => "→ IN",
                TraceDirection.Outgoing => "← OUT",
                TraceDirection.Internal => "● INT",
                _ => ""
            };

            var msg = entry.Message.Length > 50 ? entry.Message[..50] + "..." : entry.Message;
            sb.AppendLine($"| {entry.Sequence} | {entry.Timestamp:HH:mm:ss} | {arrow} | {entry.Category} | {msg} |");
        }

        if (_entries.IsEmpty)
        {
            sb.AppendLine("| - | - | - | - | (no traces) |");
        }

        return sb.ToString();
    }

    private static string? TruncateBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        return body.Length > 500 ? body[..500] + "...(truncated)" : body;
    }
}

public enum TraceDirection
{
    Incoming,
    Outgoing,
    Internal
}

public sealed record TraceEntry(
    int Sequence,
    DateTimeOffset Timestamp,
    TraceDirection Direction,
    string Category,
    string Message,
    string? Details);
