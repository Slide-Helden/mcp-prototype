using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol.Server;

namespace OpsServer;

[McpServerResourceType]
public static class OpsResources
{
    [McpServerResource(
        Name = "ops.overview",
        Title = "Ops Uebersicht",
        MimeType = "text/markdown",
        UriTemplate = "ops/overview")]
    [Description("Kurze Zusammenfassung aller Services und Einstieg in die Tools.")]
    public static string Overview(OpsState state)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] ops/overview gelesen");
        var services = state.ListServices();
        var sb = new StringBuilder();

        sb.AppendLine("# MCP Ops Demo (ohne KI, C# Edition)");
        sb.AppendLine("Der Host nutzt MCP-Tools/Resources manuell, ohne ein Modell aufzurufen.");
        sb.AppendLine("Use Case: C#-Team koordiniert Deploy/Restarts/Runbooks ohne LLM.");
        sb.AppendLine();
        sb.AppendLine("## Services");
        foreach (var s in services)
        {
            var maintenance = s.InMaintenance ? " (maintenance)" : string.Empty;
            sb.AppendLine($"- **{s.Name}** (`{s.Id}`){maintenance}");
            sb.AppendLine($"  - Status: {s.Status}, Version: {s.Version}, Instanzen: {s.Instances}");
            sb.AppendLine($"  - Latenz: {s.LatencyMs:F1} ms, Fehler/h: {s.ErrorsLastHour}");
        }

        sb.AppendLine();
        sb.AppendLine("## Schnellstart");
        sb.AppendLine("- Ressourcen: `ops/overview`, `ops/service/{id}`, `ops/runbook/{topic}`, `ops/timeline`");
        sb.AppendLine("- Tools: `ops.services.list`, `ops.service.restart`, `ops.service.deploy`, `ops.service.maintenance`, `ops.timeline.note`");
        sb.AppendLine("- Fokus: MCP ohne KI - alles wird vom Host bewusst angestossen.");
        sb.AppendLine();
        sb.AppendLine("## Runbooks (C# Alltag)");
        sb.AppendLine("- `restart-service` (wenn dotnet watch klemmt)");
        sb.AppendLine("- `fix-nullable-storm` (CS8618-Feuer loeschen)");
        sb.AppendLine("- `nuget-cache-panic` (Restore zickt im Standup)");
        sb.AppendLine("- `tests-red-green` (Integrationstests wackeln)");
        sb.AppendLine("- plus: `deploy-blue-green`, `incident-first-response`");

        return sb.ToString();
    }

    [McpServerResource(
        Name = "ops.service.detail",
        Title = "Service Detail",
        MimeType = "text/markdown",
        UriTemplate = "ops/service/{id}")]
    [Description("Detailansicht eines Dienstes (Status, Version, Fehlerrate).")]
    public static string Service(string id, OpsState state)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] ops/service/{id} gelesen");
        var svc = state.GetService(id);
        if (svc is null)
        {
            return $"Service '{id}' nicht gefunden.";
        }

        var maintenance = svc.InMaintenance ? " (maintenance)" : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"# {svc.Name} (`{svc.Id}`){maintenance}");
        sb.AppendLine($"Status: {svc.Status}");
        sb.AppendLine($"Version: {svc.Version}");
        sb.AppendLine($"Instanzen: {svc.Instances}");
        sb.AppendLine($"Latenz: {svc.LatencyMs:F1} ms");
        sb.AppendLine($"Fehler letzte Stunde: {svc.ErrorsLastHour}");
        sb.AppendLine($"Letzte Aktion (UTC): {svc.LastAction.ToString("u", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("Naechste Schritte (manuell):");
        sb.AppendLine("- `ops.service.restart` oder `ops.service.deploy` aufrufen");
        sb.AppendLine("- Runbook lesen: `ops/runbook/restart-service` oder C#-Spezial (`fix-nullable-storm`, `nuget-cache-panic`, `tests-red-green`)");
        sb.AppendLine("- Ergebnis erneut ueber diese Resource pruefen");

        return sb.ToString();
    }

    [McpServerResource(
        Name = "ops.timeline",
        Title = "Timeline",
        MimeType = "text/markdown",
        UriTemplate = "ops/timeline")]
    [Description("Letzte Ereignisse zur Demo-Session.")]
    public static string Timeline(OpsState state)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] ops/timeline gelesen");
        var events = state.LatestEvents();
        var sb = new StringBuilder();

        sb.AppendLine("# Timeline");
        foreach (var e in events)
        {
            sb.AppendLine($"- {e.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} UTC: {e.Text}");
        }

        if (events.Count == 0)
        {
            sb.AppendLine("(keine Eintraege)");
        }

        return sb.ToString();
    }

    [McpServerResource(
        Name = "ops.runbook",
        Title = "Runbook",
        MimeType = "text/markdown",
        UriTemplate = "ops/runbook/{topic}")]
    [Description("Manuelle Checkliste (Markdown). Kein Modell noetig.")]
    public static string Runbook(string topic, OpsState state)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Resource] ops/runbook/{topic} gelesen");
        return state.GetRunbook(topic);
    }
}
