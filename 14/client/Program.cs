// TestPlanConsole - Plan-Server-Client (nur Lesen)

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Text;
using System.Text.Json;

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5800/sse";
Log($"[MCP] Connecting to {url}...");
var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Plan Server",
        Endpoint = new Uri(url)
    }));

Log($"[MCP] Connected to {url}");
Console.WriteLine("Dieser Client liest nur Testplaene (Server 1). Ausfuehrung liegt auf Server 2.");

while (true)
{
    PrintMenu();
    Console.Write("> ");
    var choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await ListPlans(mcpClient);
            break;
        case "2":
            await ReadPlan(mcpClient);
            break;
        case "3":
            return;
        default:
            Console.WriteLine("Unknown choice.");
            break;
    }
}

static async Task ListPlans(McpClient client)
{
    var res = await client.ReadResourceAsync("tests/catalog");
    PrintContent(res.Contents.ToAIContents(), "Plan-Katalog");
}

static async Task ReadPlan(McpClient client)
{
    Console.Write("Plan-Slug (z. B. google-news): ");
    var slug = (Console.ReadLine() ?? "google-news").Trim();
    var res = await client.ReadResourceAsync($"tests/plan/{slug}");
    PrintContent(res.Contents.ToAIContents(), $"Planbeschreibung {slug}");
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("TestPlan Katalog-Client (Server 1)");
    Console.WriteLine(" 1) Plaene listen (Resource catalog)");
    Console.WriteLine(" 2) Plan lesen (Resource tests/plan/{slug})");
    Console.WriteLine(" 3) Exit");
}

static void PrintContent(IEnumerable<AIContent> content, string headline)
{
    var text = ExtractText(content);
    Console.WriteLine($"\n--- {headline} ---");
    Console.WriteLine(string.IsNullOrWhiteSpace(text) ? "(leer)" : text);
}

static string ExtractText(IEnumerable<AIContent> content)
{
    var sb = new StringBuilder();
    foreach (var t in content.OfType<TextContent>())
    {
        sb.AppendLine(t.Text);
    }
    if (sb.Length > 0) return sb.ToString();

    foreach (var item in content)
    {
        sb.AppendLine(JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }));
    }
    return sb.ToString();
}

static void Log(string message)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
}
