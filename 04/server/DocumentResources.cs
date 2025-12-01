using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DocServer;

[McpServerResourceType]
public static class DocumentResources
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [McpServerResource(
        Name = "docs.catalog",
        Title = "Uebersicht der Dokumente",
        MimeType = "text/markdown",
        UriTemplate = "docs/catalog")]
    [Description("Listet alle verfuegbaren Dokumente mit Tags und Inhaltsvorschau auf.")]
    public static string Catalog(DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] docs/catalog gelesen");
        var builder = new StringBuilder();
        builder.AppendLine("# Dokumenten-Katalog");
        builder.AppendLine();
        builder.AppendLine("Nutze `read_resource docs/document/{id}` fuer den Volltext.");
        builder.AppendLine();

        foreach (var doc in catalog.List())
        {
            var tags = doc.Tags.Count > 0 ? string.Join(", ", doc.Tags) : "keine Tags";
            builder.AppendLine($"- **{doc.Title}** (`{doc.Id}`)");
            builder.AppendLine($"  - Tags: {tags}");
            builder.AppendLine($"  - Preview: {doc.Summary}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    [McpServerResource(
        Name = "docs.document",
        Title = "Dokument Volltext",
        MimeType = "text/plain",
        UriTemplate = "docs/document/{id}")]
    [Description("Gibt den reinen Text eines Dokuments zurueck.")]
    public static string Document(string id, DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] docs/document/{id} gelesen");
        var doc = catalog.TryGet(id);
        if (doc is null)
        {
            return $"Dokument {id} wurde nicht gefunden.";
        }

        return doc.Content;
    }

    [McpServerResource(
        Name = "docs.summary",
        Title = "Dokument Zusammenfassung",
        MimeType = "application/json",
        UriTemplate = "docs/summary/{id}")]
    [Description("Liefert Metadaten und eine Kurzzusammenfassung als JSON.")]
    public static string Summary(string id, DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] docs/summary/{id} gelesen");
        var doc = catalog.TryGet(id);
        if (doc is null)
        {
            return JsonSerializer.Serialize(new
            {
                id,
                found = false,
                message = "Dokument nicht gefunden."
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            doc.Id,
            doc.Title,
            doc.Tags,
            doc.Summary,
            resource = $"docs/document/{doc.Id}"
        }, JsonOptions);
    }
}
