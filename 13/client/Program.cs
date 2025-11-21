// TraceConsole - MCP request/response Beobachtung (kein LLM)

using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Text;
using System.Text.Json;

var url = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5700/sse";
IMcpClient mcpClient = await McpClientFactory.CreateAsync(
    new SseClientTransport(new()
    {
        Name = "Trace Server",
        Endpoint = new Uri(url)
    }));

Console.WriteLine($"[MCP] Connected to {url}");
Console.WriteLine("Ziel: Calls ausloesen und dann trace.logs ansehen (HTTP-Level).");

while (true)
{
    PrintMenu();
    Console.Write("> ");
    var choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await CallPing(mcpClient);
            break;
        case "2":
            await CallEcho(mcpClient);
            break;
        case "3":
            await ReadTrace(mcpClient);
            break;
        case "4":
            await ListTools(mcpClient);
            break;
        case "5":
            return;
        default:
            Console.WriteLine("Unknown choice.");
            break;
    }
}

static async Task CallPing(IMcpClient client)
{
    var result = await client.CallToolAsync("trace.ping", new Dictionary<string, object?>());
    PrintContent(result.Content.ToAIContents(), "PING Antwort");
}

static async Task CallEcho(IMcpClient client)
{
    Console.Write("Nachricht fuer trace.echo: ");
    var msg = Console.ReadLine() ?? "hello trace";

    var result = await client.CallToolAsync("trace.echo", new Dictionary<string, object?>
    {
        ["message"] = msg
    });

    PrintContent(result.Content.ToAIContents(), "ECHO Antwort");
}

static async Task ReadTrace(IMcpClient client)
{
    var res = await client.ReadResourceAsync("trace/logs");
    PrintContent(res.Contents.ToAIContents(), "Server Trace Log");
}

static async Task ListTools(IMcpClient client)
{
    var tools = await client.ListToolsAsync();
    Console.WriteLine("Tools:");
    foreach (var t in tools)
    {
        Console.WriteLine($"- {t.Name} : {t.Description}");
    }
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("Trace Demo");
    Console.WriteLine(" 1) trace.ping (Tool)");
    Console.WriteLine(" 2) trace.echo (Tool)");
    Console.WriteLine(" 3) trace.logs lesen (Resource, zeigt mcp JSON-RPC HTTP-Calls)");
    Console.WriteLine(" 4) Tools listen");
    Console.WriteLine(" 5) Exit");
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

    // Fallback: falls JSON-Content als Objekt kommt
    foreach (var obj in content.OfType<ObjectContent>())
    {
        sb.AppendLine(JsonSerializer.Serialize(obj.Value, new JsonSerializerOptions { WriteIndented = true }));
    }
    return sb.ToString();
}
