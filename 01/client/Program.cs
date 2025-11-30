// Program.cs – LLM-first Edition (C# 12 / .NET 10)
// NuGet: Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI (prerelease), ModelContextProtocol (prerelease)

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.Json;

// Aliase gegen Namenskollisionen
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AIFunction = Microsoft.Extensions.AI.AIFunction;

// ---------- 1) Chat-Client auf lokales Ollama (/v1) ----------

/*
    //cloud beispiel    

    var endpoint = Environment.GetEnvironmentVariable("OLLAMA_OPENAI_ENDPOINT");
    var modelId = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

 */

// lokales beispiel

var endpoint = "http://localhost:11434/v1";
var modelId = "gpt-oss:20b";
var apiKey = "ollama";

IChatClient chat =
    new ChatClientBuilder(
        new ChatClient(
            model: modelId,
            credential: new ApiKeyCredential(apiKey),
            options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) }
        ).AsIChatClient()
    )
    .UseFunctionInvocation() // Tool-/Function-Calls aktivieren (LLM-first Voraussetzung)
    .Build();

Log($"[Chat] Using model: {modelId} @ {endpoint}");
Console.WriteLine();

// ---------- 2) MCP-Client (HTTP/SSE) zu bereits laufendem Server ----------

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5000/sse";
Log($"[MCP] Connecting to {url}...");
var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Demo HTTP Server",
        Endpoint = new Uri(url),
        // Optional: DefaultHeaders = new() { ["Authorization"] = "Bearer <token>" }
    }));

Log("[MCP] Connected.");
Console.WriteLine();

// ---------- 3) Server-Tools holen und LLM-first-Client-Tools definieren ----------

// 3a) Echte Server-Tools
var serverTools = await mcpClient.ListToolsAsync();

var serverPrompts = await mcpClient.ListPromptsAsync();
var directResources = await mcpClient.ListResourcesAsync();
var resourceTemplates = await mcpClient.ListResourceTemplatesAsync();

Log($"[MCP] Server liefert {serverTools.Count} Tool(s), {serverPrompts.Count} Prompt(s) und {directResources.Count} Resource(s) (+ {resourceTemplates.Count} Template(s)).");
if (serverTools.Count > 0)
    Log($"[MCP]   Tools: {string.Join(", ", serverTools.Select(t => t.Name))}");
if (serverPrompts.Count > 0)
    Log($"[MCP]   Prompts: {string.Join(", ", serverPrompts.Select(p => p.Name))}");
if (directResources.Count > 0)
    Log($"[MCP]   Resources: {string.Join(", ", directResources.Select(r => r.Uri))}");
if (resourceTemplates.Count > 0)
    Log($"[MCP]   Templates: {string.Join(", ", resourceTemplates.Select(t => t.UriTemplate))}");
Console.WriteLine("      Nutze :prompts, :resources, :prompt <name> oder :read <uri> für einen schnellen Einstieg.");
Console.WriteLine();

// 3b) Synthetische Client-Tools, die intern den MCP-Client nutzen
//     -> LLM kann selbstständig Ressourcen & Prompts entdecken/lesen

// Listet Ressourcen (Name, URI, MIME)
var listResourcesTool = AIFunctionFactory.Create(
    method: async () =>
    {
        var res = await mcpClient.ListResourcesAsync();
        return res.Select(r => new { r.Name, r.Uri, r.MimeType }).ToArray();
    },
    name: "mcp.list_resources",
    description: "Listet verfügbare MCP-Ressourcen (Name, URI, MIME-Typ) auf."
);

// Liest eine Ressource und gibt deren Textinhalte zurück (abgeschnitten, um Kontext zu schützen)
var readResourceTool = AIFunctionFactory.Create(
    method: async (string uri) =>
    {
        var read = await mcpClient.ReadResourceAsync(uri);
        // In AI-Contents umwandeln und Text extrahieren (nur Textteile)
        var ai = read.Contents.ToAIContents();
        var text = string.Join("\n\n", ai.OfType<TextContent>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(text)) return "(keine Textinhalte oder unbekanntes Format)";
        return text.Length <= 8000 ? text : text[..8000]; // harte Schutzgrenze
    },
    name: "mcp.read_resource",
    description: "Liest eine MCP-Resource per URI und gibt deren Textinhalt zurück."
);

// Listet verfügbare Prompts
var listPromptsTool = AIFunctionFactory.Create(
    method: async () =>
    {
        var ps = await mcpClient.ListPromptsAsync();
        return ps.Select(p => new { p.Name, p.Description }).ToArray();
    },
    name: "mcp.list_prompts",
    description: "Listet verfügbare MCP-Prompts (Name, Beschreibung) auf."
);

// Holt einen Prompt (mit optionalen Argumenten) und gibt die Textbausteine zurück
var getPromptTool = AIFunctionFactory.Create(
    method: async (string name, Dictionary<string, object?>? args) =>
    {
        var pr = await mcpClient.GetPromptAsync(name, args ?? new());
        var msgs = pr.ToChatMessages();
        // Text aus Prompt-Messages zusammenführen (non-text Messages werden übersprungen)
        var sb = new StringBuilder();
        foreach (var m in msgs)
        {
            if (!string.IsNullOrWhiteSpace(m.Text))
            {
                if (sb.Length > 0) sb.AppendLine("\n---\n");
                sb.Append(m.Text);
            }
        }
        return sb.Length > 0 ? sb.ToString() : "(Prompt enthält keine reinen Textsegmente)";
    },
    name: "mcp.get_prompt",
    description: "Lädt einen MCP-Prompt (mit optionalen Args) und gibt dessen zusammengefassten Textinhalt zurück."
);

// 3c) Tool-Bag: Server-Tools + Client-Tools zusammenführen
var toolBag = new List<AIFunction>();
toolBag.AddRange(serverTools.Cast<AIFunction>());     // MCP-Server-Tools
toolBag.Add(listResourcesTool);                       // Client-Tools (Ressourcen/Prompts)
toolBag.Add(readResourceTool);
toolBag.Add(listPromptsTool);
toolBag.Add(getPromptTool);

// ---------- 4) (Optional) Resource-Update-Notifications empfangen ----------
// Für Live-Updates (Subscriptions) kannst du den Handler wieder aktivieren:
/*
mcpClient.RegisterNotificationHandler("notifications/resources/updated",
    async (JsonRpcNotification notif, CancellationToken ct) =>
    {
        try
        {
            if (notif.Params is JsonElement el && el.TryGetProperty("uri", out var uriProp))
                Console.WriteLine($"\n[Notify] Resource updated: {uriProp.GetString()}");
        }
        catch { }
        await Task.CompletedTask;
    });
*/

// ---------- 5) REPL: LLM-first – Modell entscheidet, welche Tools/Res/Prompts es nutzt ----------

// System-Guidance: animiere das Modell, Tools zielgerichtet zu verwenden
var history = new List<AIChatMessage> {
    new(AIChatRole.System,
        "Du hast Zugriff auf MCP-Tools, -Ressourcen und -Prompts über bereitgestellte Funktionen. " +
        "Wenn dir Informationen fehlen oder Fakten geprüft werden müssen: " +
        "1) rufe zuerst 'mcp.list_resources' oder 'mcp.list_prompts' auf, " +
        "2) nutze anschließend 'mcp.read_resource' oder 'mcp.get_prompt'. " +
        "Nutze Tools nur, wenn sie relevant sind. Antworte prägnant.")
};

PrintHelp();

while (true)
{
    Console.Write("\n> ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) continue;

    if (line.Equals(":exit", StringComparison.OrdinalIgnoreCase)) break;
    if (line.Equals(":help", StringComparison.OrdinalIgnoreCase)) { PrintHelp(); continue; }

    if (line.Equals(":tools", StringComparison.OrdinalIgnoreCase))
    {
        serverTools = await mcpClient.ListToolsAsync();
        Console.WriteLine("Server-Tools:");
        foreach (var t in serverTools) Console.WriteLine($"  - {t.Name} : {t.Description}");
        continue;
    }
    if (line.Equals(":prompts", StringComparison.OrdinalIgnoreCase))
    {
        var ps = await mcpClient.ListPromptsAsync();
        Console.WriteLine("Prompts:");
        foreach (var p in ps) Console.WriteLine($"  - {p.Name} : {p.Description}");
        continue;
    }
    if (line.Equals(":resources", StringComparison.OrdinalIgnoreCase))
    {
        var templates = await mcpClient.ListResourceTemplatesAsync();
        var resources = await mcpClient.ListResourcesAsync();
        Console.WriteLine("Templates:");
        foreach (var t in templates) Console.WriteLine($"  - {t.Name} -> {t.UriTemplate}");
        Console.WriteLine("Resources:");
        foreach (var r in resources) Console.WriteLine($"  - {r.Name} -> {r.Uri}");
        continue;
    }

    // :prompt <name> [args-json] — führt einen Prompt aus (LLM darf dann weitere Tools nutzen)
    if (line.StartsWith(":prompt ", StringComparison.OrdinalIgnoreCase))
    {
        var parts = SplitOnce(line[8..].Trim());
        var promptName = parts.head;
        var argsJson = parts.tail;

        if (string.IsNullOrWhiteSpace(promptName))
        {
            Console.WriteLine("Usage: :prompt <name> [args-json]");
            continue;
        }

        var aiArgs = ParseArgs(argsJson);
        var promptResult = await mcpClient.GetPromptAsync(promptName, aiArgs);
        var promptMsgs = promptResult.ToChatMessages();

        var updates = new List<ChatResponseUpdate>();
        await foreach (var upd in chat.GetStreamingResponseAsync(
            promptMsgs,
            new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
        {
            Console.Write(upd);
            updates.Add(upd);
        }
        Console.WriteLine();
        continue;
    }

    // :read <uri> — liest Resource und gibt sie direkt in den Chat (LLM kann danach Tools nutzen)
    if (line.StartsWith(":read ", StringComparison.OrdinalIgnoreCase))
    {
        var uri = line[6..].Trim();
        if (string.IsNullOrWhiteSpace(uri))
        {
            Console.WriteLine("Usage: :read <uri>");
            continue;
        }

        var read = await mcpClient.ReadResourceAsync(uri);
        var aiContents = read.Contents.ToAIContents();
        var ctxMsg = new AIChatMessage(AIChatRole.User, aiContents);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var upd in chat.GetStreamingResponseAsync(
            new[] { ctxMsg },
            new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
        {
            Console.Write(upd);
            updates.Add(upd);
        }
        Console.WriteLine();
        continue;
    }

    // Default: freier Chat – LLM-first (mit voller Tool-Bag)
    history.Add(new(AIChatRole.User, line));

    var turnUpdates = new List<ChatResponseUpdate>();
    await foreach (var update in chat.GetStreamingResponseAsync(
        history.AsEnumerable(),
        new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
    {
        Console.Write(update);
        turnUpdates.Add(update);
    }
    Console.WriteLine();

    // Antwort + Tool-Resultate in Verlauf übernehmen
    history.AddMessages(turnUpdates);
}

// ---------------------- Hilfsfunktionen ----------------------

static (string head, string tail) SplitOnce(string s)
{
    var i = IndexOfWhitespace(s);
    if (i < 0) return (s, "");
    return (s[..i].Trim(), s[(i + 1)..].Trim());

    static int IndexOfWhitespace(string str)
    {
        for (int i = 0; i < str.Length; i++)
            if (char.IsWhiteSpace(str[i])) return i;
        return -1;
    }
}

static IReadOnlyDictionary<string, object?> ParseArgs(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
    try
    {
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = JsonElementToDotNet(p.Value);
            return dict;
        }
    }
    catch { /* ignore */ }
    Console.WriteLine("Warn: args-json konnte nicht geparst werden – es werden keine Argumente übergeben.");
    return new Dictionary<string, object?>();

    static object? JsonElementToDotNet(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(el.GetRawText()),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(el.GetRawText()),
            _ => el.GetRawText()
        };
}

static void Log(string message)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  :help                  – diese Hilfe");
    Console.WriteLine("  :exit                  – beenden");
    Console.WriteLine("  :tools                 – MCP-Server-Tools listen");
    Console.WriteLine("  :prompts               – MCP-Prompts listen");
    Console.WriteLine("  :resources             – Resource-Templates & Resources listen");
    Console.WriteLine("  :prompt <name> [json]  – Prompt laden & Antwort erzeugen (LLM kann Tools nutzen)");
    Console.WriteLine("  :read <uri>            – Resource lesen & Antwort erzeugen (LLM kann Tools nutzen)");
    Console.WriteLine("  <Text>                 – freier Chat (LLM-first, Tools/Res/Prompts autonom)");
}
