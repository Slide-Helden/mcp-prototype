using System.ComponentModel;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;

namespace OperatorServer;

[McpServerResourceType]
public static class DocumentResources
{
    [McpServerResource(
        Name = "manual.docs.catalog",
        Title = "Operator-Katalog",
        MimeType = "text/markdown",
        UriTemplate = "manual/docs/catalog")]
    [Description("Ausgangspunkt fuer menschlich gefuehrte Session.")]
    public static string Catalog(DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] manual/docs/catalog gelesen");
        var sb = new StringBuilder();
        sb.AppendLine("# Operator Katalog");
        sb.AppendLine("Dieser Katalog ist fuer manuelle Sessions gedacht.");
        sb.AppendLine();

        foreach (var doc in catalog.List())
        {
            var tags = doc.Tags.Count > 0 ? string.Join(", ", doc.Tags) : "keine";
            sb.AppendLine($"- **{doc.Title}** (`{doc.Id}`)");
            sb.AppendLine($"  - Tags: {tags}");
            sb.AppendLine($"  - Vorschau: {doc.Summary}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerResource(
        Name = "manual.docs.document",
        Title = "Dokument Volltext",
        MimeType = "text/plain",
        UriTemplate = "manual/docs/document/{id}")]
    [Description("Liefert den Dokumenteninhalt fuer die anschliessende manuelle Auswertung.")]
    public static string Document(string id, DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] manual/docs/document/{id} gelesen");
        var doc = catalog.Find(id);
        return doc?.Content ?? $"Dokument {id} nicht gefunden.";
    }
}
