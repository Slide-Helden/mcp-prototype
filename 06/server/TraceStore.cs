using System.Collections.Concurrent;
using System.Text;

namespace TraceServer;

public sealed class TraceStore
{
    private const int MaxEntries = 64;
    private readonly ConcurrentQueue<string> _entries = new();

    public void Add(string entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }
    }

    public string Dump()
    {
        var sb = new StringBuilder();
        foreach (var line in _entries)
        {
            sb.AppendLine(line);
        }
        return sb.Length == 0 ? "(no traces captured yet)" : sb.ToString();
    }
}
