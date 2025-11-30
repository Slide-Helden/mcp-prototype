// OpsConsole - MCP Demo ohne KI (C# 12 / .NET 10)

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Text;
using System.Text.Json;

const string OverviewUri = "ops/overview";
const string TimelineUri = "ops/timeline";
const string ServiceUriTemplate = "ops/service/{0}";
const string RunbookUriTemplate = "ops/runbook/{0}";

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5600/sse";
var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Ops Server (no AI)",
        Endpoint = new Uri(url)
    }));

Console.WriteLine($"[MCP] Connected to {url} (kein LLM im Spiel).");
Console.WriteLine("Dieses Client-UI stoesst Tools/Resources manuell an (C# Dev Edition: NuGet, Nullable, Tests).");
Console.WriteLine();

while (true)
{
    PrintMenu();
    Console.Write("> ");
    var choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await ShowOverviewAsync(mcpClient);
            break;
        case "2":
            await ListServicesAsync(mcpClient);
            break;
        case "3":
            await ShowServiceAsync(mcpClient);
            break;
        case "4":
            await RestartServiceAsync(mcpClient);
            break;
        case "5":
            await DeployServiceAsync(mcpClient);
            break;
        case "6":
            await AddNoteAsync(mcpClient);
            break;
        case "7":
            await ShowTimelineAsync(mcpClient);
            break;
        case "8":
            await ShowRunbookAsync(mcpClient);
            break;
        case "9":
            return;
        default:
            Console.WriteLine("Unbekannte Auswahl.");
            break;
    }
}

static async Task ShowOverviewAsync(McpClient client)
{
    var result = await client.ReadResourceAsync(OverviewUri);
    PrintText(result.Contents.ToAIContents());
}

static async Task ListServicesAsync(McpClient client)
{
    var result = await client.CallToolAsync("ops.services.list", new Dictionary<string, object?>());
    var text = ExtractText(result.Content.ToAIContents());

    if (!TryPrintServiceTable(text))
    {
        Console.WriteLine(text);
    }
}

static async Task ShowServiceAsync(McpClient client)
{
    var id = Prompt("Service-ID (z. B. api, worker, billing)");
    if (string.IsNullOrWhiteSpace(id)) return;

    var uri = string.Format(ServiceUriTemplate, id);
    var result = await client.ReadResourceAsync(uri);
    PrintText(result.Contents.ToAIContents());
}

static async Task RestartServiceAsync(McpClient client)
{
    var id = Prompt("Service-ID fuer Restart");
    if (string.IsNullOrWhiteSpace(id)) return;

    var result = await client.CallToolAsync("ops.service.restart", new Dictionary<string, object?>
    {
        ["serviceId"] = id
    });

    PrintActionResult(result.Content.ToAIContents());
}

static async Task DeployServiceAsync(McpClient client)
{
    var id = Prompt("Service-ID fuer Deployment");
    var version = Prompt("Zielversion (z. B. 1.4.3-pre)");
    var note = Prompt("Optionale Notiz (Ticket/Change) [leer lassen fuer keine]");
    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)) return;

    var result = await client.CallToolAsync("ops.service.deploy", new Dictionary<string, object?>
    {
        ["serviceId"] = id,
        ["version"] = version,
        ["note"] = note
    });

    PrintActionResult(result.Content.ToAIContents());
}

static async Task AddNoteAsync(McpClient client)
{
    var note = Prompt("Notiztext fuer Timeline");
    var actor = Prompt("Akteur (optional)");
    if (string.IsNullOrWhiteSpace(note)) return;

    var result = await client.CallToolAsync("ops.timeline.note", new Dictionary<string, object?>
    {
        ["note"] = note,
        ["actor"] = actor
    });

    PrintActionResult(result.Content.ToAIContents());
}

static async Task ShowTimelineAsync(McpClient client)
{
    var result = await client.ReadResourceAsync(TimelineUri);
    PrintText(result.Contents.ToAIContents());
}

static async Task ShowRunbookAsync(McpClient client)
{
    Console.WriteLine("Verfuegbare Topics: restart-service, deploy-blue-green, incident-first-response");
    var topic = Prompt("Runbook Topic");
    if (string.IsNullOrWhiteSpace(topic)) return;

    var uri = string.Format(RunbookUriTemplate, topic);
    var result = await client.ReadResourceAsync(uri);
    PrintText(result.Contents.ToAIContents());
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("Ops Demo - MCP ohne KI (C# Edition)");
    Console.WriteLine(" 1) Status-Uebersicht lesen (Resource)");
    Console.WriteLine(" 2) Services listen (Tool)");
    Console.WriteLine(" 3) Service-Details lesen (Resource)");
    Console.WriteLine(" 4) Service neu starten (Tool)");
    Console.WriteLine(" 5) Deployment markieren (Tool)");
    Console.WriteLine(" 6) Timeline-Notiz anlegen (Tool)");
    Console.WriteLine(" 7) Timeline anzeigen (Resource)");
    Console.WriteLine(" 8) Runbook anzeigen (Resource)");
    Console.WriteLine(" 9) Beenden");
}

static void PrintText(IEnumerable<AIContent> content)
{
    var text = ExtractText(content);
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine("(kein Textinhalt)");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine(text);
    }
}

static void PrintActionResult(IEnumerable<AIContent> content)
{
    var text = ExtractText(content);
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine("(keine Ausgabe)");
        return;
    }

    if (!TryPrintJsonSummary(text))
    {
        Console.WriteLine(text);
    }
}

static bool TryPrintServiceTable(string jsonText)
{
    try
    {
        var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

        Console.WriteLine();
        Console.WriteLine("ID        Status        Version     Inst   Lat(ms)   Err/h   Maint");
        Console.WriteLine("-------------------------------------------------------------------");

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = ReadString(item, "Id") ?? "?";
            var status = ReadString(item, "Status") ?? "?";
            var version = ReadString(item, "Version") ?? "?";
            var instances = ReadInt(item, "Instances");
            var latency = ReadDouble(item, "LatencyMs");
            var errors = ReadInt(item, "ErrorsPerHour");
            var maintenance = ReadBool(item, "InMaintenance") ? "yes" : "no";

            Console.WriteLine($"{id,-9}{status,-13}{version,-12}{instances,-6}{latency,8:F1}{errors,8}{maintenance,8}");
        }

        return true;
    }
    catch
    {
        return false;
    }
}

static bool TryPrintJsonSummary(string jsonText)
{
    try
    {
        var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

        var message = ReadString(doc.RootElement, "Message");
        if (!string.IsNullOrWhiteSpace(message))
        {
            Console.WriteLine(message);
        }

        if (doc.RootElement.TryGetProperty("Service", out var svc) && svc.ValueKind == JsonValueKind.Object)
        {
            var id = ReadString(svc, "Id") ?? "?";
            var status = ReadString(svc, "Status") ?? "?";
            var version = ReadString(svc, "Version") ?? "?";
            var errors = ReadInt(svc, "ErrorsLastHour");
            var latency = ReadDouble(svc, "LatencyMs");
            var maintenance = ReadBool(svc, "InMaintenance") ? "yes" : "no";
            Console.WriteLine($"Service: {id} | Status {status} | Version {version} | Err/h {errors} | Lat {latency:F1} ms | Maint {maintenance}");
        }

        if (doc.RootElement.TryGetProperty("TimelineEvent", out var evt) && evt.ValueKind == JsonValueKind.Object)
        {
            var ts = ReadString(evt, "Timestamp");
            var text = ReadString(evt, "Text");
            Console.WriteLine($"Timeline: {ts} - {text}");
        }

        return true;
    }
    catch
    {
        return false;
    }
}

static string ExtractText(IEnumerable<AIContent> contents)
{
    var sb = new StringBuilder();
    foreach (var t in contents.OfType<TextContent>())
    {
        if (sb.Length > 0) sb.AppendLine();
        sb.Append(t.Text);
    }
    return sb.ToString();
}

static string Prompt(string label)
{
    Console.Write($"{label}: ");
    return Console.ReadLine() ?? string.Empty;
}

static string? ReadString(JsonElement el, string property)
{
    if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String)
    {
        return val.GetString();
    }

    var camel = char.ToLowerInvariant(property[0]) + property[1..];
    if (el.TryGetProperty(camel, out var camelVal) && camelVal.ValueKind == JsonValueKind.String)
    {
        return camelVal.GetString();
    }

    return null;
}

static int ReadInt(JsonElement el, string property)
{
    if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var i))
    {
        return i;
    }

    var camel = char.ToLowerInvariant(property[0]) + property[1..];
    if (el.TryGetProperty(camel, out var camelVal) && camelVal.ValueKind == JsonValueKind.Number && camelVal.TryGetInt32(out var j))
    {
        return j;
    }

    return 0;
}

static double ReadDouble(JsonElement el, string property)
{
    if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var d))
    {
        return d;
    }

    var camel = char.ToLowerInvariant(property[0]) + property[1..];
    if (el.TryGetProperty(camel, out var camelVal) && camelVal.ValueKind == JsonValueKind.Number && camelVal.TryGetDouble(out var e))
    {
        return e;
    }

    return 0;
}

static bool ReadBool(JsonElement el, string property)
{
    if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.True) return true;
    if (val.ValueKind == JsonValueKind.False) return false;

    var camel = char.ToLowerInvariant(property[0]) + property[1..];
    if (el.TryGetProperty(camel, out var camelVal) && camelVal.ValueKind == JsonValueKind.True) return true;
    if (camelVal.ValueKind == JsonValueKind.False) return false;

    return false;
}
