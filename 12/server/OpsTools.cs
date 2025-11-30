using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace OpsServer;

[McpServerToolType]
public static class OpsTools
{
    [McpServerTool(Name = "ops.services.list")]
    [Description("Listet alle Services samt Status, Version, Latenz und Fehlerrate.")]
    public static IEnumerable<object> ListServices(OpsState state)
    {
        return state.ListServices()
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Status,
                s.Version,
                s.InMaintenance,
                s.Instances,
                LatencyMs = s.LatencyMs,
                ErrorsPerHour = s.ErrorsLastHour
            })
            .ToList();
    }

    [McpServerTool(Name = "ops.service.restart")]
    [Description("Setzt einen Restart-Impuls fuer einen Service und dokumentiert die Timeline.")]
    public static OpsActionResult Restart(
        [Description("Service-ID, z. B. api oder worker")] string serviceId,
        OpsState state) =>
        state.RestartService(serviceId);

    [McpServerTool(Name = "ops.service.deploy")]
    [Description("Markiert ein Deployment mit Version und optionaler Notiz.")]
    public static OpsActionResult Deploy(
        [Description("Service-ID, z. B. api oder billing")] string serviceId,
        [Description("Zielversion, z. B. 1.4.3-pre")] string version,
        [Description("Optionale Notiz (Change, Ticket, Erwartung).")] string? note,
        OpsState state) =>
        state.DeployService(serviceId, version, note);

    [McpServerTool(Name = "ops.service.maintenance")]
    [Description("Aktiviert oder deaktiviert den Maintenance-Modus fuer einen Service.")]
    public static OpsActionResult Maintenance(
        [Description("Service-ID, z. B. billing")] string serviceId,
        [Description("true = Maintenance aktiv, false = beenden")] bool enabled,
        OpsState state) =>
        state.ToggleMaintenance(serviceId, enabled);

    [McpServerTool(Name = "ops.timeline.note")]
    [Description("Haengt eine Freitext-Notiz an die Timeline an (ohne KI).")]
    public static OpsActionResult Note(
        [Description("Notiztext, erscheint in der Timeline.")] string note,
        [Description("Optionaler Akteur (Name, Rolle).")] string? actor,
        OpsState state) =>
        state.RecordNote(note, actor);
}
