// Program.cs - LLM-first Rezept-Demo mit Live-Trace (C# 12 / .NET 10)
// Zeigt die ECHTE MCP-Protokoll-Kommunikation via SDK-Logging

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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

// ---------- SDK-Logging konfigurieren ----------
var logLevel = LogLevel.Debug; // hier Loglevel ändern

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "[HH:mm:ss.fff] ";
        options.SingleLine = true;
        options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
    });
    builder.SetMinimumLevel(logLevel);
    // MCP-spezifische Kategorien auf Trace setzen fuer maximale Details
    builder.AddFilter("ModelContextProtocol", logLevel);
});

var logger = loggerFactory.CreateLogger("Demo04");

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
Log($"[Chat] Using model: {modelId} @ {endpoint}");
Console.WriteLine();

// ---------- 2) MCP-Client zum Dokumenten-Server (mit echtem SDK-Logging) ----------

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5000/sse";
logger.LogInformation("Verbinde zu MCP-Server: {Url}", url);

var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Name = "Document HTTP Server",
            Endpoint = new Uri(url)
        },
        loggerFactory  // <-- Echtes SDK-Logging aktiviert!
    ),
    new McpClientOptions
    {
        ClientInfo = new() { Name = "Demo04-Client", Version = "1.0.0" }
    },
    loggerFactory  // <-- Auch fuer den McpClient selbst
);

logger.LogInformation("MCP-Verbindung hergestellt");
Console.WriteLine();

// ---------- 3) Inventory: Tools, Prompts, Resources ----------
// Das SDK loggt jetzt automatisch alle MCP-Aufrufe!

var serverTools = await mcpClient.ListToolsAsync();
var serverPrompts = await mcpClient.ListPromptsAsync();
var directResources = await mcpClient.ListResourcesAsync();
var resourceTemplates = await mcpClient.ListResourceTemplatesAsync();

Console.WriteLine();
Log($"[MCP] {serverTools.Count} Tool(s), {serverPrompts.Count} Prompt(s), {directResources.Count} Resource(s), {resourceTemplates.Count} Template(s).");
if (serverTools.Count > 0)
    Log($"[MCP]   Tools: {string.Join(", ", serverTools.Select(t => t.Name))}");
if (serverPrompts.Count > 0)
    Log($"[MCP]   Prompts: {string.Join(", ", serverPrompts.Select(p => p.Name))}");
if (directResources.Count > 0)
    Log($"[MCP]   Resources: {string.Join(", ", directResources.Select(r => r.Uri))}");
if (resourceTemplates.Count > 0)
    Log($"[MCP]   Templates: {string.Join(", ", resourceTemplates.Select(t => t.UriTemplate))}");
Console.WriteLine();
PrintHelp();

// ---------- 4) Client-seitige Hilfs-Tools ----------
// SDK-Logging zeigt automatisch alle MCP-Aufrufe im Detail!

var listResourcesTool = AIFunctionFactory.Create(
    method: async () =>
    {
        logger.LogDebug("Tool mcp.list_resources aufgerufen");
        var resources = await mcpClient.ListResourcesAsync();
        return resources.Select(r => new { r.Name, r.Uri, r.MimeType }).ToArray();
    },
    name: "mcp.list_resources",
    description: "Listet verfuegbare Ressourcen (Name, URI, MIME) des Dokumentenservers."
);

var readResourceTool = AIFunctionFactory.Create(
    method: async (string uri) =>
    {
        logger.LogDebug("Tool mcp.read_resource aufgerufen: {Uri}", uri);
        var read = await mcpClient.ReadResourceAsync(uri);
        var ai = read.Contents.ToAIContents();
        var text = string.Join("\n\n", ai.OfType<TextContent>().Select(t => t.Text));
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
        logger.LogDebug("Tool mcp.list_prompts aufgerufen");
        var prompts = await mcpClient.ListPromptsAsync();
        return prompts.Select(p => new { p.Name, p.Description }).ToArray();
    },
    name: "mcp.list_prompts",
    description: "Listet verfuegbare MCP-Prompts des Dokumentenservers auf."
);

var getPromptTool = AIFunctionFactory.Create(
    method: async (string name, Dictionary<string, object?>? args) =>
    {
        logger.LogDebug("Tool mcp.get_prompt aufgerufen: {Name}", name);
        var prompt = await mcpClient.GetPromptAsync(name, args ?? new());
        var msgs = prompt.ToChatMessages();
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
        logger.LogDebug("Tool trace.server aufgerufen");
        var read = await mcpClient.ReadResourceAsync("trace/logs");
        var text = string.Join("\n", read.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
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
// SDK-Logging zeigt automatisch alle MCP-Protokoll-Details!

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

    // Trace-Befehle - jetzt mit echtem SDK-Logging
    if (line.Equals(":trace:server", StringComparison.OrdinalIgnoreCase))
    {
        var traceRead = await mcpClient.ReadResourceAsync("trace/logs");
        var traceText = string.Join("\n", traceRead.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
        Console.WriteLine("\n=== SERVER TRACE ===");
        Console.WriteLine(traceText);
        continue;
    }

    if (line.Equals(":trace:debug", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("[Trace] Log-Level auf DEBUG gesetzt - maximale Details");
        // Hinweis: Log-Level kann zur Laufzeit nicht einfach geaendert werden,
        // aber wir informieren den Nutzer
        Console.WriteLine("        (Neustart mit angepasstem LogLevel erforderlich)");
        continue;
    }

    if (line.Equals(":trace:stats", StringComparison.OrdinalIgnoreCase))
    {
        var statsResult = await mcpClient.CallToolAsync("trace.stats", new Dictionary<string, object?>());
        var statsText = string.Join("\n", statsResult.Content.ToAIContents().OfType<TextContent>().Select(t => t.Text));
        Console.WriteLine("\n=== TRACE STATISTIKEN ===");
        Console.WriteLine(statsText);
        continue;
    }

    if (line.Equals(":tools", StringComparison.OrdinalIgnoreCase))
    {
        serverTools = await mcpClient.ListToolsAsync();
        Console.WriteLine("Server-Tools:");
        foreach (var t in serverTools) Console.WriteLine($"  - {t.Name} : {t.Description}");
        continue;
    }

    if (line.Equals(":prompts", StringComparison.OrdinalIgnoreCase))
    {
        var prompts = await mcpClient.ListPromptsAsync();
        Console.WriteLine("Prompts:");
        foreach (var p in prompts) Console.WriteLine($"  - {p.Name} : {p.Description}");
        continue;
    }

    if (line.Equals(":resources", StringComparison.OrdinalIgnoreCase))
    {
        resourceTemplates = await mcpClient.ListResourceTemplatesAsync();
        directResources = await mcpClient.ListResourcesAsync();
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
        var promptResult = await mcpClient.GetPromptAsync(promptName, aiArgs);
        var promptMsgs = promptResult.ToChatMessages();

        logger.LogInformation("LLM-Request mit {Count} Prompt-Messages", promptMsgs.Count);
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in chat.GetStreamingResponseAsync(
            promptMsgs,
            new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
        {
            Console.Write(update);
            updates.Add(update);
        }
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

        var read = await mcpClient.ReadResourceAsync(uri);
        var aiContents = read.Contents.ToAIContents();
        var ctxMsg = new AIChatMessage(AIChatRole.User, aiContents);

        logger.LogInformation("LLM-Request mit Resource-Inhalt");
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in chat.GetStreamingResponseAsync(
            new[] { ctxMsg },
            new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
        {
            Console.Write(update);
            updates.Add(update);
        }
        Console.WriteLine();
        continue;
    }

    // Freier Chat
    history.Add(new(AIChatRole.User, line));
    logger.LogInformation("LLM-Request: {MessageCount} Messages, {ToolCount} Tools", history.Count, toolBag.Count);

    var turnUpdates = new List<ChatResponseUpdate>();
    await foreach (var update in chat.GetStreamingResponseAsync(
        history.AsEnumerable(),
        new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
    {
        Console.Write(update);
        turnUpdates.Add(update);
    }
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

static void Log(string message)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
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
    Console.WriteLine("Trace-Commands (SDK-Logging):");
    Console.WriteLine("  :trace:server   - Server-seitigen Trace abrufen");
    Console.WriteLine("  :trace:stats    - Trace-Statistiken vom Server");
    Console.WriteLine("  :trace:debug    - Hinweis: Log-Level Anpassung");
    Console.WriteLine();
    Console.WriteLine("Das SDK-Logging zeigt automatisch alle MCP-Protokoll-Details in der Konsole!");
}

