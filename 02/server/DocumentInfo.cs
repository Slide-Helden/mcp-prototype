using System;
using System.Collections.Generic;
using System.Linq;

namespace DocServer;

public sealed class DocumentInfo
{
    public DocumentInfo(string id, string title, IReadOnlyList<string> tags, string filePath, string content, string summary)
    {
        Id = id;
        Title = title;
        Tags = tags;
        FilePath = filePath;
        Content = content;
        Summary = summary;
    }

    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<string> Tags { get; }

    public string FilePath { get; }

    public string Content { get; }

    public string Summary { get; }

    public bool MatchesKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var term = keyword.Trim();

        return Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || Content.Contains(term, StringComparison.OrdinalIgnoreCase)
            || Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public object ToSummaryDto() => new
    {
        id = Id,
        title = Title,
        tags = Tags,
        summary = Summary
    };
}
