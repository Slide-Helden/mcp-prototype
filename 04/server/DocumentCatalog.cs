using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Hosting;

namespace DocServer;

public sealed class DocumentCatalog
{
    private readonly IReadOnlyDictionary<string, DocumentInfo> _documents;
    private readonly string _documentsPath;

    public DocumentCatalog(IHostEnvironment environment)
    {
        _documentsPath = Path.Combine(environment.ContentRootPath, "Documents");
        _documents = LoadDocuments(_documentsPath);
    }

    public IReadOnlyCollection<DocumentInfo> List() =>
        _documents.Values
            .OrderBy(doc => doc.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public DocumentInfo? TryGet(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        _documents.TryGetValue(Normalize(id), out var doc);
        return doc;
    }

    public IEnumerable<DocumentInfo> Search(string? keyword)
    {
        return List().Where(doc => doc.MatchesKeyword(keyword ?? string.Empty));
    }

    public DocumentInfo? PickRandom()
    {
        if (_documents.Count == 0)
        {
            return null;
        }

        var index = Random.Shared.Next(_documents.Count);
        return _documents.ElementAt(index).Value;
    }

    public string? TryReadContent(string? id) => TryGet(id)?.Content;

    private static IReadOnlyDictionary<string, DocumentInfo> LoadDocuments(string documentsPath)
    {
        if (!Directory.Exists(documentsPath))
        {
            return new Dictionary<string, DocumentInfo>();
        }

        var result = new Dictionary<string, DocumentInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(documentsPath, "*.txt", SearchOption.AllDirectories))
        {
            var doc = ParseDocument(filePath);
            result[doc.Id] = doc;
        }

        return result;
    }

    private static DocumentInfo ParseDocument(string filePath)
    {
        var id = Normalize(Path.GetFileNameWithoutExtension(filePath));
        var lines = File.ReadAllLines(filePath);

        var title = lines.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"Document {id}";
        }

        var tagLine = lines.Skip(1).FirstOrDefault() ?? string.Empty;
        var tags = new List<string>();
        var contentStartIndex = 1;

        if (tagLine.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
        {
            tags = tagLine[5..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(tag => tag.ToLowerInvariant())
                .ToList();
            contentStartIndex = 2;
        }

        var content = string.Join(Environment.NewLine, lines.Skip(contentStartIndex));

        var summary = content.Length > 200
            ? content[..200] + "..."
            : content;

        return new DocumentInfo(id, title, tags, filePath, content, summary);
    }

    private static string Normalize(string value) =>
        value.Trim().Replace(' ', '-').ToLowerInvariant();
}
