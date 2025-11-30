// Program.cs - LLM-first Rezept-Demo mit Live-Trace (C# 12 / .NET 10)
// Zeigt die Kommunikation zwischen Client, Server und LLM in Echtzeit

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.Json;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AIFunction = Microsoft.Extensions.AI.AIFunction;

// ---------- Client-seitiger Trace-Logger mit Live-Output ----------
var clientTrace = new ClientTraceLogger(liveOutput: true); // Live-Trace standardmaessig AN
var liveTraceEnabled = true;

// ---------- 1) Chat-Client (Ollama / OpenAI kompatibel) ----------

var endpoint = Environment.GetEnvironmentVariable("OLLAMA_OPENAI_ENDPOINT") ?? "http://localhost:11434/v1";
var modelId = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "gpt-oss:20b";
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "ollama";

IChatClient chat =
    new ChatClientBuilder(
        new ChatClient(
            model: modelId,
            credential: new ApiKeyCredential(apiKey),
            options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) }
        ).AsIChatClient()
    )
    .UseFunctionInvocation()
    .Build();

PrintBanner();
Console.WriteLine($"[Chat] Using model: {modelId} @ {endpoint}");
Console.WriteLine();

// ---------- 2) MCP-Client zum Dokumenten-Server ----------

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5200/sse";
clientTrace.Log("MCP", "CLIENT", $"Verbinde zu Server: {url}");

var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Document HTTP Server",
        Endpoint = new Uri(url)
    }));

clientTrace.Log("MCP", "CLIENT", "SSE-Verbindung hergestellt");
Console.WriteLine("[MCP] Connected to Document server.");
Console.WriteLine();

// ---------- 3) Inventory: Tools, Prompts, Resources ----------

clientTrace.Log("MCP", "CLIENT->SERVER", "tools/list");
var serverTools = await mcpClient.ListToolsAsync();
clientTrace.Log("MCP", "SERVER->CLIENT", $"tools/list: {serverTools.Count} Tools");

clientTrace.Log("MCP", "CLIENT->SERVER", "prompts/list");
var serverPrompts = await mcpClient.ListPromptsAsync();
clientTrace.Log("MCP", "SERVER->CLIENT", $"prompts/list: {serverPrompts.Count} Prompts");

clientTrace.Log("MCP", "CLIENT->SERVER", "resources/list");
var directResources = await mcpClient.ListResourcesAsync();
clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/list: {directResources.Count} Resources");

clientTrace.Log("MCP", "CLIENT->SERVER", "resources/templates/list");
var resourceTemplates = await mcpClient.ListResourceTemplatesAsync();
clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/templates/list: {resourceTemplates.Count} Templates");

Console.WriteLine($"[MCP] {serverTools.Count} Tool(s), {serverPrompts.Count} Prompt(s), {directResources.Count} Resource(s), {resourceTemplates.Count} Template(s).");
Console.WriteLine();
PrintHelp();

// ---------- 4) Client-seitige Hilfs-Tools mit Live-Tracing ----------

var listResourcesTool = AIFunctionFactory.Create(
    method: async () =>
    {
        clientTrace.Log("TOOL", "LLM->CLIENT", "mcp.list_resources aufgerufen");
        clientTrace.Log("MCP", "CLIENT->SERVER", "resources/list");
        var resources = await mcpClient.ListResourcesAsync();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/list: {resources.Count} Eintraege");
        return resources.Select(r => new { r.Name, r.Uri, r.MimeType }).ToArray();
    },
    name: "mcp.list_resources",
    description: "Listet verfuegbare Ressourcen (Name, URI, MIME) des Dokumentenservers."
);

var readResourceTool = AIFunctionFactory.Create(
    method: async (string uri) =>
    {
        clientTrace.Log("TOOL", "LLM->CLIENT", $"mcp.read_resource({uri})");
        clientTrace.Log("MCP", "CLIENT->SERVER", $"resources/read uri={uri}");
        var read = await mcpClient.ReadResourceAsync(uri);
        var ai = read.Contents.ToAIContents();
        var text = string.Join("\n\n", ai.OfType<TextContent>().Select(t => t.Text));
        clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/read: {text.Length} Zeichen empfangen");
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(keine Textinhalte oder unbekanntes Format)";
        }
        return text.Length <= 8000 ? text : text[..8000];
    },
    name: "mcp.read_resource",
    description: "Laedt eine Resource ueber ihre URI und liefert Textinhalt (begrenzte Laenge)."
);

var listPromptsTool = AIFunctionFactory.Create(
    method: async () =>
    {
        clientTrace.Log("TOOL", "LLM->CLIENT", "mcp.list_prompts aufgerufen");
        clientTrace.Log("MCP", "CLIENT->SERVER", "prompts/list");
        var prompts = await mcpClient.ListPromptsAsync();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"prompts/list: {prompts.Count} Eintraege");
        return prompts.Select(p => new { p.Name, p.Description }).ToArray();
    },
    name: "mcp.list_prompts",
    description: "Listet verfuegbare MCP-Prompts des Dokumentenservers auf."
);

var getPromptTool = AIFunctionFactory.Create(
    method: async (string name, Dictionary<string, object?>? args) =>
    {
        clientTrace.Log("TOOL", "LLM->CLIENT", $"mcp.get_prompt({name})");
        clientTrace.Log("MCP", "CLIENT->SERVER", $"prompts/get name={name}");
        var prompt = await mcpClient.GetPromptAsync(name, args ?? new());
        var msgs = prompt.ToChatMessages();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"prompts/get: {msgs.Count} Messages");
        var sb = new StringBuilder();
        foreach (var message in msgs)
        {
            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                if (sb.Length > 0) sb.AppendLine("\n---\n");
                sb.Append(message.Text);
            }
        }
        return sb.Length > 0 ? sb.ToString() : "(Prompt enthaelt keinen reinen Text)";
    },
    name: "mcp.get_prompt",
    description: "Ruft einen Prompt ab und liefert seinen Textinhalt."
);

// Tool: Server-Traces abrufen
var getServerTraceTool = AIFunctionFactory.Create(
    method: async () =>
    {
        clientTrace.Log("TOOL", "LLM->CLIENT", "trace.server aufgerufen");
        clientTrace.Log("MCP", "CLIENT->SERVER", "resources/read uri=trace/logs");
        var read = await mcpClient.ReadResourceAsync("trace/logs");
        var text = string.Join("\n", read.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
        clientTrace.Log("MCP", "SERVER->CLIENT", $"trace/logs: {text.Length} Zeichen");
        return text;
    },
    name: "trace.server",
    description: "Ruft die Server-seitigen MCP-Kommunikations-Traces ab."
);

var toolBag = new List<AIFunction>();
toolBag.AddRange(serverTools.Cast<AIFunction>());
toolBag.Add(listResourcesTool);
toolBag.Add(readResourceTool);
toolBag.Add(listPromptsTool);
toolBag.Add(getPromptTool);
toolBag.Add(getServerTraceTool);

// ---------- 5) REPL ----------

var history = new List<AIChatMessage>
{
    new(AIChatRole.System,
        "Du bist ein kulinarischer Assistent. Deine Datenquelle ist ein Dokumenten-Server." +
        "WICHTIG - BEFOLGE DIESEN PROZESS:" +
        "1. Um zu wissen, welche Rezepte existieren, MUSST du zuerst die Resource 'docs/catalog' lesen (nutze mcp.read_resource)." +
        "2. Du erhaeltst eine Liste von Dokumenten mit IDs." +
        "3. WENN der Nutzer nach einem Rezept fragt (z.B. 'Nudeln'), suche die passende ID aus dem Katalog." +
        "4. Lade den Text des Rezepts, indem du die ID in das Template einsetzt: 'docs/document/{id}'." +
        "   BEISPIEL: Wenn ID='gnocchi' ist, rufe 'mcp.read_resource' mit 'docs/document/gnocchi' auf." +
        "5. Gib erst dann die Kochanweisungen." +
        "Sage niemals, dass du den Text nicht hast, ohne diesen Prozess probiert zu haben.")
};

while (true)
{
    Console.Write("\n> ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) continue;

    if (line.Equals(":exit", StringComparison.OrdinalIgnoreCase)) break;
    if (line.Equals(":help", StringComparison.OrdinalIgnoreCase))
    {
        PrintHelp();
        continue;
    }

    // Trace-Befehle
    if (line.Equals(":trace", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(clientTrace.Dump());
        continue;
    }

    if (line.Equals(":trace:server", StringComparison.OrdinalIgnoreCase))
    {
        clientTrace.Log("MCP", "CLIENT->SERVER", "resources/read uri=trace/logs");
        var traceRead = await mcpClient.ReadResourceAsync("trace/logs");
        var traceText = string.Join("\n", traceRead.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
        clientTrace.Log("MCP", "SERVER->CLIENT", $"trace/logs erhalten");
        Console.WriteLine("\n=== SERVER TRACE ===");
        Console.WriteLine(traceText);
        continue;
    }

    if (line.Equals(":trace:live", StringComparison.OrdinalIgnoreCase) || line.Equals(":trace:on", StringComparison.OrdinalIgnoreCase))
    {
        liveTraceEnabled = true;
        clientTrace.LiveOutput = true;
        Console.WriteLine("[Trace] Live-Trace AKTIVIERT - alle Requests werden in Echtzeit angezeigt");
        continue;
    }

    if (line.Equals(":trace:off", StringComparison.OrdinalIgnoreCase))
    {
        liveTraceEnabled = false;
        clientTrace.LiveOutput = false;
        Console.WriteLine("[Trace] Live-Trace deaktiviert");
        continue;
    }

    if (line.Equals(":trace:clear", StringComparison.OrdinalIgnoreCase))
    {
        clientTrace.Clear();
        Console.WriteLine("[Trace] Client-Trace geleert");
        continue;
    }

    if (line.Equals(":trace:stats", StringComparison.OrdinalIgnoreCase))
    {
        clientTrace.Log("MCP", "CLIENT->SERVER", "tools/call trace.stats");
        var statsResult = await mcpClient.CallToolAsync("trace.stats", new Dictionary<string, object?>());
        var statsText = string.Join("\n", statsResult.Content.ToAIContents().OfType<TextContent>().Select(t => t.Text));
        clientTrace.Log("MCP", "SERVER->CLIENT", "trace.stats Ergebnis");
        Console.WriteLine("\n=== TRACE STATISTIKEN ===");
        Console.WriteLine(statsText);
        continue;
    }

    if (line.Equals(":tools", StringComparison.OrdinalIgnoreCase))
    {
        clientTrace.Log("MCP", "CLIENT->SERVER", "tools/list");
        serverTools = await mcpClient.ListToolsAsync();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"tools/list: {serverTools.Count} Tools");
        Console.WriteLine("Server-Tools:");
        foreach (var t in serverTools) Console.WriteLine($"  - {t.Name} : {t.Description}");
        continue;
    }

    if (line.Equals(":prompts", StringComparison.OrdinalIgnoreCase))
    {
        clientTrace.Log("MCP", "CLIENT->SERVER", "prompts/list");
        var prompts = await mcpClient.ListPromptsAsync();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"prompts/list: {prompts.Count} Prompts");
        Console.WriteLine("Prompts:");
        foreach (var p in prompts) Console.WriteLine($"  - {p.Name} : {p.Description}");
        continue;
    }

    if (line.Equals(":resources", StringComparison.OrdinalIgnoreCase))
    {
        clientTrace.Log("MCP", "CLIENT->SERVER", "resources/templates/list");
        resourceTemplates = await mcpClient.ListResourceTemplatesAsync();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/templates/list: {resourceTemplates.Count} Templates");

        clientTrace.Log("MCP", "CLIENT->SERVER", "resources/list");
        directResources = await mcpClient.ListResourcesAsync();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/list: {directResources.Count} Resources");

        Console.WriteLine("Templates:");
        foreach (var t in resourceTemplates) Console.WriteLine($"  - {t.Name} -> {t.UriTemplate}");
        Console.WriteLine("Resources:");
        foreach (var r in directResources) Console.WriteLine($"  - {r.Name} -> {r.Uri}");
        continue;
    }

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
        clientTrace.Log("MCP", "CLIENT->SERVER", $"prompts/get name={promptName}");
        var promptResult = await mcpClient.GetPromptAsync(promptName, aiArgs);
        var promptMsgs = promptResult.ToChatMessages();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"prompts/get: {promptMsgs.Count} Messages");

        clientTrace.Log("LLM", "CLIENT->LLM", $"Chat-Request mit {promptMsgs.Count} Messages");
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in chat.GetStreamingResponseAsync(
            promptMsgs,
            new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
        {
            Console.Write(update);
            updates.Add(update);
        }
        clientTrace.Log("LLM", "LLM->CLIENT", $"Streaming-Antwort beendet");
        Console.WriteLine();
        continue;
    }

    if (line.StartsWith(":read ", StringComparison.OrdinalIgnoreCase))
    {
        var uri = line[6..].Trim();
        if (string.IsNullOrWhiteSpace(uri))
        {
            Console.WriteLine("Usage: :read <uri>");
            continue;
        }

        clientTrace.Log("MCP", "CLIENT->SERVER", $"resources/read uri={uri}");
        var read = await mcpClient.ReadResourceAsync(uri);
        var aiContents = read.Contents.ToAIContents();
        clientTrace.Log("MCP", "SERVER->CLIENT", $"resources/read erhalten");

        var ctxMsg = new AIChatMessage(AIChatRole.User, aiContents);

        clientTrace.Log("LLM", "CLIENT->LLM", "Chat-Request mit Resource-Inhalt");
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in chat.GetStreamingResponseAsync(
            new[] { ctxMsg },
            new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
        {
            Console.Write(update);
            updates.Add(update);
        }
        clientTrace.Log("LLM", "LLM->CLIENT", "Streaming-Antwort beendet");
        Console.WriteLine();
        continue;
    }

    // Freier Chat
    history.Add(new(AIChatRole.User, line));
    clientTrace.Log("LLM", "USER->CLIENT", $"Nutzer-Eingabe: {(line.Length > 50 ? line[..50] + "..." : line)}");
    clientTrace.Log("LLM", "CLIENT->LLM", $"Chat-Request mit {history.Count} Messages + {toolBag.Count} Tools");

    var turnUpdates = new List<ChatResponseUpdate>();
    await foreach (var update in chat.GetStreamingResponseAsync(
        history.AsEnumerable(),
        new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
    {
        Console.Write(update);
        turnUpdates.Add(update);
    }
    clientTrace.Log("LLM", "LLM->CLIENT", "Streaming-Antwort beendet");
    Console.WriteLine();

    history.AddMessages(turnUpdates);
}

// ---------- Hilfsfunktionen ----------

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
    catch
    {
        Console.WriteLine("Warn: args-json konnte nicht geparst werden.");
    }
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

static void PrintBanner()
{
    Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║      Demo 04: Rezept-Assistent mit Live MCP Communication Trace               ║");
    Console.WriteLine("╠════════════════════════════════════════════════════════════════════════════════╣");
    Console.WriteLine("║                                                                                 ║");
    Console.WriteLine("║  Diese Demo zeigt in ECHTZEIT die Kommunikation zwischen:                      ║");
    Console.WriteLine("║                                                                                 ║");
    Console.WriteLine("║    ┌──────────┐      MCP/SSE       ┌──────────┐      HTTP/JSON      ┌─────┐    ║");
    Console.WriteLine("║    │  CLIENT  │◄──────────────────►│  SERVER  │◄──────────────────►│ LLM │    ║");
    Console.WriteLine("║    └──────────┘                    └──────────┘                    └─────┘    ║");
    Console.WriteLine("║         ▲                                                              ▲       ║");
    Console.WriteLine("║         │ Eingabe                                          Tool-Calls │       ║");
    Console.WriteLine("║         ▼                                                              ▼       ║");
    Console.WriteLine("║    ┌──────────┐                                                                ║");
    Console.WriteLine("║    │   USER   │   Alle Requests werden live in der Konsole angezeigt!         ║");
    Console.WriteLine("║    └──────────┘                                                                ║");
    Console.WriteLine("║                                                                                 ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝");
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  :help           - Hilfe anzeigen");
    Console.WriteLine("  :exit           - Programm beenden");
    Console.WriteLine("  :tools          - Server-Tools anzeigen");
    Console.WriteLine("  :prompts        - Prompts anzeigen");
    Console.WriteLine("  :resources      - Resources und Templates anzeigen");
    Console.WriteLine("  :prompt <name>  - Prompt ausfuehren");
    Console.WriteLine("  :read <uri>     - Resource lesen");
    Console.WriteLine("  <Text>          - Freier Chat (Modell nutzt Tools eigenstaendig)");
    Console.WriteLine();
    Console.WriteLine("Trace-Commands:");
    Console.WriteLine("  :trace          - Gesamten Trace-Verlauf anzeigen");
    Console.WriteLine("  :trace:server   - Server-seitigen Trace abrufen");
    Console.WriteLine("  :trace:live     - Live-Trace aktivieren (Requests in Echtzeit)");
    Console.WriteLine("  :trace:off      - Live-Trace deaktivieren");
    Console.WriteLine("  :trace:clear    - Trace-Verlauf leeren");
    Console.WriteLine("  :trace:stats    - Trace-Statistiken vom Server");
}

// ---------- Client Trace Logger mit Live-Output ----------

class ClientTraceLogger
{
    private readonly List<(DateTimeOffset Time, string Category, string Direction, string Message)> _entries = new();
    private readonly object _lock = new();
    private int _maxEntries = 200;

    public bool Enabled { get; set; } = true;
    public bool LiveOutput { get; set; }

    public ClientTraceLogger(bool liveOutput = false)
    {
        LiveOutput = liveOutput;
    }

    public void Log(string category, string direction, string message)
    {
        if (!Enabled) return;

        var now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            _entries.Add((now, category, direction, message));
            while (_entries.Count > _maxEntries)
            {
                _entries.RemoveAt(0);
            }
        }

        // Live-Output: Zeigt jeden Request sofort in der Konsole
        if (LiveOutput)
        {
            var arrow = direction switch
            {
                var d when d.Contains("->") && d.Contains("SERVER") => "──►",
                var d when d.Contains("->") && d.Contains("LLM") => "══►",
                var d when d.Contains("<-") || d.Contains("SERVER->CLIENT") => "◄──",
                var d when d.Contains("LLM->CLIENT") => "◄══",
                _ => "◆◆◆"
            };

            var color = category switch
            {
                "MCP" => ConsoleColor.Cyan,
                "LLM" => ConsoleColor.Yellow,
                "TOOL" => ConsoleColor.Magenta,
                _ => ConsoleColor.Gray
            };

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"  [{now:HH:mm:ss.fff}] {arrow} [{category}] {direction}: {message}");
            Console.ForegroundColor = originalColor;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                         CLIENT COMMUNICATION TRACE                             ║");
        sb.AppendLine("╠════════════════════════════════════════════════════════════════════════════════╣");

        lock (_lock)
        {
            var seq = 0;
            foreach (var (time, category, direction, message) in _entries)
            {
                seq++;
                var arrow = direction switch
                {
                    var d when d.Contains("->") => "──►",
                    var d when d.Contains("<-") => "◄──",
                    _ => "◆◆◆"
                };

                var cat = category.PadRight(4);
                var dir = direction.PadRight(15);
                var msg = message.Length > 45 ? message[..45] + "..." : message;

                sb.AppendLine($"║ [{seq:D3}] {time:HH:mm:ss.fff} {arrow} [{cat}] {dir} {msg}");
            }

            if (_entries.Count == 0)
            {
                sb.AppendLine("║ (no traces captured yet)                                                       ║");
            }
        }

        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        return sb.ToString();
    }
}
