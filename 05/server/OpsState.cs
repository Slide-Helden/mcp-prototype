using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpsServer;

public sealed class OpsState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, OpsService> _services;
    private readonly List<OpsEvent> _timeline;
    private readonly Dictionary<string, string> _runbooks;

    public OpsState()
    {
        _services = new Dictionary<string, OpsService>(StringComparer.OrdinalIgnoreCase)
        {
            ["web"] = new("web", "ASP.NET Frontend", "9.0.0-preview1", "degraded", inMaintenance: false, instances: 3, latencyMs: 148, errorsLastHour: 5),
            ["worker"] = new("worker", "Background Worker (Hangfire)", "1.12.0", "running", inMaintenance: false, instances: 4, latencyMs: 64, errorsLastHour: 1),
            ["nuget"] = new("nuget", "Private NuGet Feed", "2025.11", "running", inMaintenance: false, instances: 2, latencyMs: 32, errorsLastHour: 0),
            ["build"] = new("build", "Build Agent", "2.1.7", "maintenance", inMaintenance: true, instances: 1, latencyMs: 95, errorsLastHour: 0),
            ["tests"] = new("tests", "Integration Test Runner", "0.8.4", "degraded", inMaintenance: false, instances: 1, latencyMs: 220, errorsLastHour: 8),
        };

        _timeline = new List<OpsEvent>();

        _runbooks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["restart-service"] = BuildRestartRunbook(),
            ["deploy-blue-green"] = BuildDeployRunbook(),
            ["incident-first-response"] = BuildIncidentRunbook(),
            ["fix-nullable-storm"] = BuildNullableRunbook(),
            ["nuget-cache-panic"] = BuildNugetRunbook(),
            ["tests-red-green"] = BuildTestsRunbook()
        };

        AddEvent("Demo-Ops-Server gestartet (keine KI, nur MCP).");
        AddEvent("C# Dev-Lagebericht: VS fragt wieder nach Workload-Updates, App laeuft trotzdem.");
        AddEvent("Runbooks geladen: restart-service, deploy-blue-green, incident-first-response, fix-nullable-storm, nuget-cache-panic, tests-red-green.");
    }

    public IReadOnlyList<OpsService> ListServices()
    {
        lock (_sync)
        {
            return _services.Values
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Clone())
                .ToList();
        }
    }

    public OpsService? GetService(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = Normalize(id);
        lock (_sync)
        {
            if (_services.TryGetValue(key, out var service))
            {
                return service.Clone();
            }
        }
        return null;
    }

    public OpsActionResult RestartService(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return OpsActionResult.NotFound(id);
        }

        var key = Normalize(id);
        lock (_sync)
        {
            if (!_services.TryGetValue(key, out var service))
            {
                return OpsActionResult.NotFound(id);
            }

            service.Status = service.InMaintenance ? "maintenance" : "running";
            service.ErrorsLastHour = Math.Max(0, (int)Math.Round(service.ErrorsLastHour * 0.45, MidpointRounding.AwayFromZero));
            service.LatencyMs = Math.Max(18, Math.Round(service.LatencyMs * 0.82, 1));
            service.LastAction = DateTimeOffset.UtcNow;

            var evt = AddEvent($"Restart fuer {service.Name} bestaetigt.");
            return OpsActionResult.Success($"Restart fuer {service.Name} abgeschlossen. Fehlerlast reduziert, Status wieder {service.Status}.", service.Clone(), evt);
        }
    }

    public OpsActionResult DeployService(string id, string version, string? note)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            return OpsActionResult.NotFound(id);
        }

        var key = Normalize(id);
        lock (_sync)
        {
            if (!_services.TryGetValue(key, out var service))
            {
                return OpsActionResult.NotFound(id);
            }

            var previous = service.Version;
            service.Version = version.Trim();
            service.Status = service.InMaintenance ? "maintenance" : "running";
            service.LastAction = DateTimeOffset.UtcNow;
            service.ErrorsLastHour = Math.Max(0, service.ErrorsLastHour - 1);

            var message = $"Deployment {service.Version} fuer {service.Name} markiert (vorher {previous}).";
            if (!string.IsNullOrWhiteSpace(note))
            {
                message += $" Notiz: {note.Trim()}";
            }

            var evt = AddEvent(message);
            return OpsActionResult.Success(message, service.Clone(), evt);
        }
    }

    public OpsActionResult ToggleMaintenance(string id, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return OpsActionResult.NotFound(id);
        }

        var key = Normalize(id);
        lock (_sync)
        {
            if (!_services.TryGetValue(key, out var service))
            {
                return OpsActionResult.NotFound(id);
            }

            service.InMaintenance = enabled;
            service.Status = enabled ? "maintenance" : "running";
            service.LastAction = DateTimeOffset.UtcNow;

            var evt = AddEvent($"Maintenance-Flag fuer {service.Name} = {enabled} gesetzt.");
            return OpsActionResult.Success($"Maintenance-Modus fuer {service.Name} jetzt {(enabled ? "aktiv" : "aus")}.", service.Clone(), evt);
        }
    }

    public OpsActionResult RecordNote(string note, string? actor)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? "(leere Notiz)" : note.Trim();
        if (!string.IsNullOrWhiteSpace(actor))
        {
            trimmed = $"{actor.Trim()}: {trimmed}";
        }

        var evt = AddEvent(trimmed);
        return OpsActionResult.Success("Timeline aktualisiert.", null, evt);
    }

    public IReadOnlyList<OpsEvent> LatestEvents(int take = 8)
    {
        lock (_sync)
        {
            return _timeline
                .OrderByDescending(e => e.Timestamp)
                .Take(take)
                .Select(e => e with { })
                .ToList();
        }
    }

    public string GetRunbook(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return "Topic fehlt.";

        var key = Normalize(topic);
        lock (_sync)
        {
            return _runbooks.TryGetValue(key, out var runbook)
                ? runbook
                : $"Kein Runbook fuer '{topic}' gefunden. Verfuegbar: {string.Join(", ", _runbooks.Keys)}";
        }
    }

    private OpsEvent AddEvent(string text)
    {
        var evt = new OpsEvent(DateTimeOffset.UtcNow, text);
        lock (_sync)
        {
            _timeline.Add(evt);
            const int limit = 30;
            if (_timeline.Count > limit)
            {
                _timeline.RemoveRange(0, _timeline.Count - limit);
            }
        }
        return evt;
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    private static string BuildRestartRunbook()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runbook: Restart Service");
        sb.AppendLine("Ziel: Dienst sauber neu starten, ohne KI/LLM.");
        sb.AppendLine("1. `ops.services.list` aufrufen und Status pruefen.");
        sb.AppendLine("2. Optional Maintenance setzen: Tool `ops.service.maintenance`.");
        sb.AppendLine("3. Restart triggern: Tool `ops.service.restart` mit serviceId.");
        sb.AppendLine("4. `ops/service/{id}` lesen und Timeline ueberwachen.");
        sb.AppendLine("5. Maintenance wieder entfernen, falls gesetzt.");
        return sb.ToString();
    }

    private static string BuildDeployRunbook()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runbook: Deploy (Blue/Green light)");
        sb.AppendLine("Ziel: Kleine Deploy-Demo ohne KI, nur MCP-Aktionen.");
        sb.AppendLine("- Pruefe `ops/overview` fuer Grundzustand.");
        sb.AppendLine("- Tool `ops.service.deploy` mit Version setzen (z. B. 1.4.3-pre).");
        sb.AppendLine("- Per Resource `ops/service/{id}` kontrollieren, ob Status stabil bleibt.");
        sb.AppendLine("- Eventuell Timeline-Notiz hinzufuegen ueber `ops.timeline.note`.");
        return sb.ToString();
    }

    private static string BuildIncidentRunbook()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runbook: Incident First Response");
        sb.AppendLine("Ziel: Manuelle Erstreaktion demonstrieren (keine KI).");
        sb.AppendLine("1. Status ueber `ops/overview` und `ops.timeline` pruefen.");
        sb.AppendLine("2. Betroffene Services ueber `ops.services.list` identifizieren.");
        sb.AppendLine("3. Details ueber `ops/service/{id}` lesen (Fehlerlast beachten).");
        sb.AppendLine("4. Falls noetig Maintenance setzen: `ops.service.maintenance`.");
        sb.AppendLine("5. Restart oder Deploy ausloesen und als Notiz dokumentieren.");
        sb.AppendLine("6. Abschlussnotiz verfassen: Tool `ops.timeline.note`.");
        return sb.ToString();
    }

    private static string BuildNullableRunbook()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runbook: Nullable-Warnungen eindampfen");
        sb.AppendLine("Ziel: CS8618/CS8602-Flut managen, ohne das Team zu verunsichern.");
        sb.AppendLine("1. `ops/service/web` lesen: steht der Frontend-Baum in Flammen?");
        sb.AppendLine("2. Maintenance fuer `build` falls noetig aktivieren (`ops.service.maintenance`).");
        sb.AppendLine("3. Tool `ops.service.restart` fuer `tests` nutzen, um stale Prozesse zu killen.");
        sb.AppendLine("4. Neue Version eintragen (Tool `ops.service.deploy`) mit Hinweis \"nullable cleanup\".");
        sb.AppendLine("5. Timeline-Notiz hinterlassen: \"CS8618 auf spaeter vertagt\".");
        return sb.ToString();
    }

    private static string BuildNugetRunbook()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runbook: NuGet Cache Panic");
        sb.AppendLine("Ziel: Private Feed beruhigen, wenn Restore im Standup nicht klappt.");
        sb.AppendLine("1. `ops/service/nuget` lesen, ob Latenzen hochgehen.");
        sb.AppendLine("2. Deployment fuer `nuget` markieren (z. B. Version 2025.11.1-hotfix).");
        sb.AppendLine("3. Timeline-Notiz: \"dotnet restore (lokal) bitte mit --disable-parallel versuchen\".");
        sb.AppendLine("4. Danach `ops.services.list` checken und ggf. Maintenance fuer `build` deaktivieren.");
        return sb.ToString();
    }

    private static string BuildTestsRunbook()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runbook: Tests wieder gruen bekommen");
        sb.AppendLine("Ziel: Integrationstests enthaerten, ohne KI-Rat einzuholen.");
        sb.AppendLine("1. `ops/service/tests` lesen, Fehlerrate notieren.");
        sb.AppendLine("2. Tool `ops.service.restart` fuer `tests` ausfuehren (manchmal haengt dotnet test).");
        sb.AppendLine("3. Wenn Frontend flaky ist: `ops.service.deploy` fuer `web` mit Tag \"retry-logging\".");
        sb.AppendLine("4. Timeline-Notiz mit Ticket-Referenz hinzufuegen.");
        sb.AppendLine("5. Nach 2 Minuten erneut `ops/service/tests` lesen.");
        return sb.ToString();
    }
}

public sealed class OpsService
{
    public OpsService(string id, string name, string version, string status, bool inMaintenance, int instances, double latencyMs, int errorsLastHour)
    {
        Id = id;
        Name = name;
        Version = version;
        Status = status;
        InMaintenance = inMaintenance;
        Instances = instances;
        LatencyMs = latencyMs;
        ErrorsLastHour = errorsLastHour;
        LastAction = DateTimeOffset.UtcNow;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; set; }
    public string Status { get; set; }
    public bool InMaintenance { get; set; }
    public int Instances { get; set; }
    public double LatencyMs { get; set; }
    public int ErrorsLastHour { get; set; }
    public DateTimeOffset LastAction { get; set; }

    public OpsService Clone() =>
        new(Id, Name, Version, Status, InMaintenance, Instances, LatencyMs, ErrorsLastHour)
        {
            LastAction = LastAction
        };
}

public sealed record OpsEvent(DateTimeOffset Timestamp, string Text);

public sealed class OpsActionResult
{
    public OpsActionResult(string message, OpsService? service, OpsEvent? timelineEvent)
    {
        Message = message;
        Service = service;
        TimelineEvent = timelineEvent;
    }

    public string Message { get; }
    public OpsService? Service { get; }
    public OpsEvent? TimelineEvent { get; }

    public static OpsActionResult NotFound(string id) =>
        new($"Service '{id}' nicht gefunden.", null, null);

    public static OpsActionResult Success(string message, OpsService? service, OpsEvent? evt) =>
        new(message, service, evt);
}
