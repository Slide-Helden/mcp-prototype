using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Hosting;

namespace OperatorServer;

public sealed class DocumentCatalog
{
    private readonly IReadOnlyDictionary<string, DocumentInfo> _documents;

    public DocumentCatalog(IHostEnvironment environment)
    {
        var documentsPath = Path.Combine(environment.ContentRootPath, "Documents");
        _documents = LoadDocuments(documentsPath);
    }

    public IReadOnlyCollection<DocumentInfo> List() =>
        _documents.Values.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public IEnumerable<DocumentInfo> Search(string keyword) =>
        List().Where(doc => doc.Matches(keyword));

    public DocumentInfo? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        _documents.TryGetValue(Normalize(id), out var doc);
        return doc;
    }

    private static IReadOnlyDictionary<string, DocumentInfo> LoadDocuments(string path)
    {
        if (!Directory.Exists(path))
        {
            return new Dictionary<string, DocumentInfo>();
        }

        var dict = new Dictionary<string, DocumentInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(path, "*.txt", SearchOption.TopDirectoryOnly))
        {
            var id = Normalize(Path.GetFileNameWithoutExtension(file));
            var lines = File.ReadAllLines(file);

            var title = lines.ElementAtOrDefault(0)?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = id;
            }

            var tagLine = lines.ElementAtOrDefault(1) ?? string.Empty;
            var tags = Array.Empty<string>();
            var contentStart = 1;

            if (tagLine.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
            {
                tags = tagLine[5..]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToArray();
                contentStart = 2;
            }

            var content = string.Join(Environment.NewLine, lines.Skip(contentStart));
            dict[id] = new DocumentInfo(id, title, tags, content);
        }

        return dict;
    }

    private static string Normalize(string value) =>
        value.Trim().Replace(' ', '-').ToLowerInvariant();
}
