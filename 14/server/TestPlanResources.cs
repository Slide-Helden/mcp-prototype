using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace TestPlanServer;

[McpServerResourceType]
public static class TestPlanResources
{
    private static readonly string DocumentRoot = Path.Combine(AppContext.BaseDirectory, "Documents");

    [McpServerResource(
        Name = "tests.catalog",
        Title = "Plan-Katalog",
        MimeType = "text/markdown",
        UriTemplate = "tests/catalog")]
    [Description("Listet alle verfuegbaren Testplaene als Markdown auf.")]
    public static string Catalog()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Testplan-Katalog");
        builder.AppendLine();

        if (!Directory.Exists(DocumentRoot))
        {
            builder.AppendLine("- (kein Dokumenten-Ordner gefunden)");
            return builder.ToString();
        }

        var files = Directory.GetFiles(DocumentRoot, "*.md", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            builder.AppendLine("- (keine Plaene gefunden)");
            return builder.ToString();
        }

        foreach (var file in files.OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var firstLine = ReadFirstNonEmptyLine(file) ?? "(ohne Titel)";
            builder.AppendLine($"- **{firstLine}** (`{name}`)");
            builder.AppendLine($"  - Resource: `tests/plan/{name}`");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    [McpServerResource(
        Name = "tests.plan.google-news",
        Title = "Plan: Google News check",
        MimeType = "text/markdown",
        UriTemplate = "tests/plan/google-news")]
    [Description("Beschreibt den LLM-first Testplan fuer den Google-News-Check.")]
    public static string GoogleNewsPlan()
    {
        var path = Path.Combine(DocumentRoot, "google-news-plan.md");
        if (!File.Exists(path))
        {
            return "# Plan fehlt\nDas Plan-Dokument konnte nicht gefunden werden.";
        }

        return File.ReadAllText(path);
    }

    private static string? ReadFirstNonEmptyLine(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                return trimmed.Trim('#', ' ').Trim();
            }
        }

        return null;
    }
}
