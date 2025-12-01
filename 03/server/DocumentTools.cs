using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace OperatorServer;

[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "manual.docs.search")]
    [Description("Manuelle Suche ueber Stichwort, liefert Trefferliste.")]
    public static IEnumerable<object> Search(
        [Description("Suchbegriff fuer Volltext und Tags.")] string keyword,
        DocumentCatalog catalog)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] manual.docs.search aufgerufen (keyword={keyword ?? "null"})");
        return catalog.Search(keyword)
            .Select(doc => new
            {
                doc.Id,
                doc.Title,
                doc.Summary,
                doc.Tags
            })
            .ToList();
    }
}
