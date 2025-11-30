using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DocServer;

[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "docs.search")]
    [Description("Durchsucht den Dokumentkatalog nach einem Stichwort und liefert Treffer mit Zusammenfassung.")]
    public static IEnumerable<object> Search(
        [Description("Stichwort fuer Volltextsuche (Titel, Inhalt, Tags).")] string keyword,
        DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] docs.search aufgerufen (keyword={keyword ?? "null"})");
        if (string.IsNullOrWhiteSpace(keyword))
        {
            keyword = string.Empty;
        }

        var matches = catalog.Search(keyword)
            .Select(doc => new
            {
                doc.Id,
                doc.Title,
                doc.Summary,
                doc.Tags
            });

        return matches.ToList();
    }

    [McpServerTool(Name = "docs.random")]
    [Description("Waehlt ein zufaelliges Dokument aus und liefert eine Kurzzusammenfassung.")]
    public static object SuggestRandom(DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] docs.random aufgerufen");
        var doc = catalog.PickRandom();
        if (doc is null)
        {
            return new
            {
                message = "Es wurden keine Dokumente gefunden."
            };
        }

        return new
        {
            doc.Id,
            doc.Title,
            doc.Tags,
            doc.Summary,
            recommendation = $"Lies das Dokument ueber read_resource docs/document/{doc.Id}"
        };
    }
}
