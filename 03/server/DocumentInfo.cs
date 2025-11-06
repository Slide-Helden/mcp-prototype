using System;
using System.Collections.Generic;
using System.Linq;

namespace OperatorServer;

public sealed class DocumentInfo
{
    public DocumentInfo(string id, string title, IReadOnlyList<string> tags, string content)
    {
        Id = id;
        Title = title;
        Tags = tags;
        Content = content;
        Summary = content.Length > 180 ? content[..180] + "..." : content;
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<string> Tags { get; }
    public string Content { get; }
    public string Summary { get; }

    public bool Matches(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        return Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
