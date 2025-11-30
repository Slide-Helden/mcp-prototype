// Program.cs - LLM-first Document Demo (C# 12 / .NET 10)
// NuGet: Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, ModelContextProtocol

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Linq;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AIFunction = Microsoft.Extensions.AI.AIFunction;

// ---------- 1) Chat-Client (Ollama / OpenAI kompatibel) ----------

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
    .UseFunctionInvocation()
    .Build();

Console.WriteLine($"[Chat] Using model: {modelId} @ {endpoint}");
Console.WriteLine();

// ---------- 2) MCP-Client zum Dokumenten-Server ----------

var url = "http://localhost:5200/sse";
IMcpClient mcpClient = await McpClientFactory.CreateAsync(
    new SseClientTransport(new()
    {
        Name = "Document HTTP Server",
        Endpoint = new Uri(url)
    }));

Console.WriteLine("[MCP] Connected to Document server.");
Console.WriteLine();

// ---------- 3) Inventory: Tools, Prompts, Resources ----------

IList<McpClientTool> serverTools = await mcpClient.ListToolsAsync();
var serverPrompts = await mcpClient.ListPromptsAsync();
var directResources = await mcpClient.ListResourcesAsync();
var resourceTemplates = await mcpClient.ListResourceTemplatesAsync();

Console.WriteLine($"[MCP] {serverTools.Count} Tool(s), {serverPrompts.Count} Prompt(s), {directResources.Count} Resource(s), {resourceTemplates.Count} Template(s).");
Console.WriteLine("      Commands: :tools, :prompts, :resources, :prompt <name>, :read <uri>");
Console.WriteLine();

// ---------- 4) Client-seitige Hilfs-Tools ----------

var listResourcesTool = AIFunctionFactory.Create(
    method: async () =>
    {
        var resources = await mcpClient.ListResourcesAsync();
        return resources.Select(r => new { r.Name, r.Uri, r.MimeType }).ToArray();
    },
    name: "mcp.list_resources",
    description: "Listet verfuegbare Ressourcen (Name, URI, MIME) des Dokumentenservers."
);

var readResourceTool = AIFunctionFactory.Create(
    method: async (string uri) =>
    {
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
        var prompts = await mcpClient.ListPromptsAsync();
        return prompts.Select(p => new { p.Name, p.Description }).ToArray();
    },
    name: "mcp.list_prompts",
    description: "Listet verfuegbare MCP-Prompts des Dokumentenservers auf."
);

var getPromptTool = AIFunctionFactory.Create(
    method: async (string name, Dictionary<string, object?>? args) =>
    {
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

var toolBag = new List<AIFunction>();
toolBag.AddRange(serverTools.Cast<AIFunction>());
toolBag.Add(listResourcesTool);
toolBag.Add(readResourceTool);
toolBag.Add(listPromptsTool);
toolBag.Add(getPromptTool);

// ---------- 5) Dokument-Events (optional) ----------

/*
mcpClient.RegisterNotificationHandler("notifications/resources/updated",
    async (JsonRpcNotification notif, CancellationToken ct) =>
    {
        if (notif.Params is JsonElement el && el.TryGetProperty("uri", out var uriProp))
        {
            Console.WriteLine($"\n[Notify] Resource updated: {uriProp.GetString()}");
        }
        await Task.CompletedTask;
    });
*/

// ---------- 6) REPL ----------

var history = new List<AIChatMessage>
{
    new(AIChatRole.System,
        "Du bist eine dokumentzentrierte Assistenz. " +
        "Zuerst pruefst du den Katalog ueber mcp.list_resources oder docs/catalog. " +
        "Nutze docs.search und docs.summary um Dokumente zu identifizieren. " +
        "Lies den Volltext ueber docs/document/{id}, bevor du Witze zitierst. " +
        "Beziehe dich auf Quellen und fasse dich praezise.")
};

PrintHelp();

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
        IList<AIChatMessage> promptMsgs = promptResult.ToChatMessages();

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

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  :help                  - Hilfe anzeigen");
    Console.WriteLine("  :exit                  - Programm beenden");
    Console.WriteLine("  :tools                 - Server-Tools anzeigen");
    Console.WriteLine("  :prompts               - Prompts anzeigen");
    Console.WriteLine("  :resources             - Resources und Templates anzeigen");
    Console.WriteLine("  :prompt <name> [json]  - Prompt ausfuehren");
    Console.WriteLine("  :read <uri>            - Resource lesen");
    Console.WriteLine("  <Text>                 - Freier Chat (Modell nutzt Tools eigenstaendig)");
}
