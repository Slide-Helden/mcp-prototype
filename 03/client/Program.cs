// Program.cs - Orchestrator-first MCP Demo (C# 12 / .NET 10)

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

const string CatalogUri = "manual/docs/catalog";
const string DocumentUriTemplate = "manual/docs/document/{0}";

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
    .Build();

Console.WriteLine($"[Chat] Orchestrator-first manual mode using model {modelId}");

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5400/sse";
var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Operator Server",
        Endpoint = new Uri(url)
    }));

Console.WriteLine("[MCP] Connected. Operator (Orchestrator-first) chooses the flow.");

var collectedDocs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

while (true)
{
    Console.WriteLine();
    Console.WriteLine("Operator Menu");
    Console.WriteLine(" 1) Liste Katalog (Resource)");
    Console.WriteLine(" 2) Suche per Tool");
    Console.WriteLine(" 3) Dokument lesen");
    Console.WriteLine(" 4) Gesammelte Dokumente anzeigen");
    Console.WriteLine(" 5) Zusammenfassung per LLM erzeugen");
    Console.WriteLine(" 6) Beenden");
    Console.Write("> Auswahl: ");

    var choice = Console.ReadLine();
    switch (choice)
    {
        case "1":
            await ShowCatalogAsync(mcpClient);
            break;
        case "2":
            await RunSearchAsync(mcpClient);
            break;
        case "3":
            await ReadDocumentAsync(mcpClient, collectedDocs);
            break;
        case "4":
            ShowCollected(collectedDocs);
            break;
        case "5":
            await SummarizeAsync(chat, collectedDocs);
            break;
        case "6":
            return;
        default:
            Console.WriteLine("Unbekannte Auswahl.");
            break;
    }
}

static async Task ShowCatalogAsync(McpClient client)
{
    var result = await client.ReadResourceAsync(CatalogUri);
    var text = string.Join("\n", result.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
    Console.WriteLine();
    Console.WriteLine(text);
}

static async Task RunSearchAsync(McpClient client)
{
    Console.Write("Suchbegriff: ");
    var keyword = Console.ReadLine() ?? string.Empty;

    try
    {
        var callResult = await client.CallToolAsync("manual.docs.search", new Dictionary<string, object?>
        {
            ["keyword"] = keyword
        });

        var text = string.Join("\n", callResult.Content.ToAIContents().OfType<TextContent>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine("Keine Treffer oder Ausgabe leer.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(text);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Toolaufruf fehlgeschlagen: {ex.Message}");
    }
}

static async Task ReadDocumentAsync(McpClient client, IDictionary<string, string> collected)
{
    Console.Write("Dokument-ID: ");
    var id = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(id)) return;

    var uri = string.Format(DocumentUriTemplate, id.Trim());
    var result = await client.ReadResourceAsync(uri);
    var text = string.Join("\n", result.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
    Console.WriteLine();
    Console.WriteLine(text);

    collected[id.Trim()] = text;
    Console.WriteLine($"\n[Manual] Dokument {id} zur Sammlung hinzugefügt.");
}

static void ShowCollected(IDictionary<string, string> collected)
{
    if (collected.Count == 0)
    {
        Console.WriteLine("Keine Dokumente gesammelt.");
        return;
    }

    foreach (var pair in collected)
    {
        Console.WriteLine($"\n--- {pair.Key} ---");
        Console.WriteLine(pair.Value);
    }
}

static async Task SummarizeAsync(IChatClient chat, IDictionary<string, string> collected)
{
    if (collected.Count == 0)
    {
        Console.WriteLine("Keine Inhalte fuer Zusammenfassung vorhanden.");
        return;
    }

    var sb = new StringBuilder();
    sb.AppendLine("Erstelle eine Moderationszusammenfassung fuer folgende Dokumente.");
    sb.AppendLine("Fokus: betone, dass dies eine manuell gefuehrte Session war.");
    foreach (var (id, content) in collected)
    {
        sb.AppendLine($"\nDokument {id}:");
        sb.AppendLine(content);
    }

    var messages = new List<AIChatMessage>
    {
        new(AIChatRole.System, "Du bist ein Assistent, der Moderationsnotizen zusammenfasst."),
        new(AIChatRole.User, sb.ToString())
    };

    Console.WriteLine("\n--- Zusammenfassung ---");
    await foreach (var update in chat.GetStreamingResponseAsync(messages))
    {
        Console.Write(update);
    }
    Console.WriteLine();
}
