// Demo 07: Multi-Server LLM-first Orchestrierung
// Ein Aufruf -> LLM nutzt automatisch beide MCP-Server (Plan + Executor)

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
var logLevel = LogLevel.Debug; // hier Loglevel aendern (Trace fuer JSON-RPC Details)

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "[HH:mm:ss.fff] ";
        options.SingleLine = true;
        options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
    });
    builder.SetMinimumLevel(logLevel);
    builder.AddFilter("ModelContextProtocol", logLevel);
});

var logger = loggerFactory.CreateLogger("Demo07");

// ---------- 1) LLM Chat-Client (GitHub Models / OpenAI-kompatibel) ----------

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
Log($"[LLM] Using model: {modelId} @ {endpoint}");
Console.WriteLine();

// ---------- 2) MCP-Clients: Plan-Server + Exec-Server ----------

var planUrl = Environment.GetEnvironmentVariable("MCP_PLAN_SERVER_URL") ?? "http://localhost:5000/sse";
var execUrl = Environment.GetEnvironmentVariable("MCP_EXEC_SERVER_URL") ?? "http://localhost:5001/sse";

// Plan-Server verbinden
logger.LogInformation("Verbinde zu Plan-Server: {Url}", planUrl);
var planClient = await McpClient.CreateAsync(
    new HttpClientTransport(
        new HttpClientTransportOptions { Name = "Plan Server", Endpoint = new Uri(planUrl) },
        loggerFactory
    ),
    new McpClientOptions { ClientInfo = new() { Name = "Demo07-Client", Version = "1.0.0" } },
    loggerFactory
);

// Exec-Server verbinden
logger.LogInformation("Verbinde zu Exec-Server: {Url}", execUrl);
var execClient = await McpClient.CreateAsync(
    new HttpClientTransport(
        new HttpClientTransportOptions { Name = "Exec Server", Endpoint = new Uri(execUrl) },
        loggerFactory
    ),
    new McpClientOptions { ClientInfo = new() { Name = "Demo07-Client", Version = "1.0.0" } },
    loggerFactory
);

// Inventory ausgeben
var planResources = await planClient.ListResourcesAsync();
var execTools = await execClient.ListToolsAsync();

Console.WriteLine();
Log($"[MCP] Plan-Server: {planResources.Count} Resource(s)");
Log($"[MCP] Exec-Server: {execTools.Count} Tool(s)");
Console.WriteLine();

// ---------- 3) LLM-Tools: Wrapper fuer beide MCP-Server ----------

// Plan-Server Tools (Resources als Tools exponieren)
var listPlansTool = AIFunctionFactory.Create(
    method: async () =>
    {
        logger.LogDebug("Tool plans.list aufgerufen -> Plan-Server");
        var res = await planClient.ReadResourceAsync("tests/catalog");
        return string.Join("\n", res.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
    },
    name: "plans.list",
    description: "Listet alle verfuegbaren Testplaene vom Plan-Server."
);

var readPlanTool = AIFunctionFactory.Create(
    method: async (string planName) =>
    {
        logger.LogDebug("Tool plans.read aufgerufen -> Plan-Server (plan={Plan})", planName);
        var res = await planClient.ReadResourceAsync($"tests/plan/{planName}");
        return string.Join("\n", res.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text));
    },
    name: "plans.read",
    description: "Liest einen spezifischen Testplan vom Plan-Server. Parameter: planName - der Kurzname aus dem Katalog (z.B. 'google-news', NICHT 'google-news-plan')"
);

// Exec-Server Tools (Server-Tools direkt weiterleiten)
var listExecToolsTool = AIFunctionFactory.Create(
    method: async () =>
    {
        logger.LogDebug("Tool exec.tools aufgerufen -> Exec-Server");
        var tools = await execClient.ListToolsAsync();
        return tools.Select(t => new { t.Name, t.Description }).ToArray();
    },
    name: "exec.tools",
    description: "Listet verfuegbare Ausfuehrungs-Tools vom Exec-Server."
);

var runTestTool = AIFunctionFactory.Create(
    method: async (string planName) =>
    {
        logger.LogDebug("Tool exec.run aufgerufen -> Exec-Server (plan={Plan})", planName);
        var result = await execClient.CallToolAsync("tests.run", new Dictionary<string, object?> { ["plan"] = planName });
        return string.Join("\n", result.Content.ToAIContents().OfType<TextContent>().Select(t => t.Text));
    },
    name: "exec.run",
    description: "Fuehrt einen Testplan auf dem Exec-Server aus. Parameter: planName - der Kurzname (z.B. 'google-news', NICHT 'google-news-plan')"
);

// Tool-Bag fuer LLM
var toolBag = new List<AIFunction> { listPlansTool, readPlanTool, listExecToolsTool, runTestTool };

// ---------- 4) System-Prompt: LLM orchestriert beide Server ----------

var systemPrompt = @"Du bist ein Test-Orchestrator mit Zugriff auf ZWEI MCP-Server:

1. PLAN-SERVER (Port 5000) - Testplan-Katalog:
   - plans.list: Zeigt alle verfuegbaren Testplaene
   - plans.read(planName): Liest Details eines Plans

2. EXEC-SERVER (Port 5001) - Testplan-Ausfuehrung:
   - exec.tools: Zeigt verfuegbare Ausfuehrungs-Tools
   - exec.run(planName): Fuehrt einen Testplan aus

WICHTIGER WORKFLOW bei Anfragen wie 'Fuehre den google-news Test aus':
1. Erst plans.list aufrufen um verfuegbare Plaene zu sehen
2. Dann plans.read('google-news') um den Plan zu verstehen
3. Dann exec.run('google-news') um den Test auszufuehren
4. Ergebnis zusammenfassen

Du demonstrierst Multi-Server-Orchestrierung: Ein Client, zwei spezialisierte Server.
Erklaere kurz welchen Server du gerade ansprichst.";

var history = new List<AIChatMessage> { new(AIChatRole.System, systemPrompt) };

// ---------- 5) REPL ----------

PrintHelp();

while (true)
{
    Console.Write("\n> ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) continue;

    if (line.Equals(":exit", StringComparison.OrdinalIgnoreCase)) break;
    if (line.Equals(":help", StringComparison.OrdinalIgnoreCase)) { PrintHelp(); continue; }

    if (line.Equals(":plans", StringComparison.OrdinalIgnoreCase))
    {
        var res = await planClient.ReadResourceAsync("tests/catalog");
        Console.WriteLine("\n=== Testplaene (Plan-Server) ===");
        Console.WriteLine(string.Join("\n", res.Contents.ToAIContents().OfType<TextContent>().Select(t => t.Text)));
        continue;
    }

    if (line.Equals(":tools", StringComparison.OrdinalIgnoreCase))
    {
        var tools = await execClient.ListToolsAsync();
        Console.WriteLine("\n=== Tools (Exec-Server) ===");
        foreach (var t in tools) Console.WriteLine($"  - {t.Name}: {t.Description}");
        continue;
    }

    // LLM-first: Freier Chat - LLM entscheidet welche Server/Tools es nutzt
    history.Add(new(AIChatRole.User, line));
    logger.LogInformation("LLM-Request: {MessageCount} Messages, {ToolCount} Tools", history.Count, toolBag.Count);

    var updates = new List<ChatResponseUpdate>();
    await foreach (var update in chat.GetStreamingResponseAsync(
        history.AsEnumerable(),
        new ChatOptions { Tools = [.. toolBag], AllowMultipleToolCalls = true }))
    {
        Console.Write(update);
        updates.Add(update);
    }
    Console.WriteLine();

    history.AddMessages(updates);
}

// ---------- Hilfsfunktionen ----------

static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

static void PrintBanner()
{
    Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║      Demo 07: Multi-Server LLM-first Orchestrierung                           ║");
    Console.WriteLine("╠════════════════════════════════════════════════════════════════════════════════╣");
    Console.WriteLine("║                                                                                 ║");
    Console.WriteLine("║  Ein Aufruf -> LLM orchestriert automatisch ZWEI MCP-Server:                   ║");
    Console.WriteLine("║                                                                                 ║");
    Console.WriteLine("║              ┌─────────────────┐                                                ║");
    Console.WriteLine("║         ┌───►│  Plan-Server    │  (Resources: Testplan-Katalog)                ║");
    Console.WriteLine("║         │    └─────────────────┘                                                ║");
    Console.WriteLine("║    ┌────┴────┐                                                                  ║");
    Console.WriteLine("║    │   LLM   │  entscheidet automatisch                                         ║");
    Console.WriteLine("║    └────┬────┘                                                                  ║");
    Console.WriteLine("║         │    ┌─────────────────┐                                                ║");
    Console.WriteLine("║         └───►│  Exec-Server    │  (Tools: Testplan-Ausfuehrung)                ║");
    Console.WriteLine("║              └─────────────────┘                                                ║");
    Console.WriteLine("║                                                                                 ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝");
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  :help   - Diese Hilfe");
    Console.WriteLine("  :exit   - Beenden");
    Console.WriteLine("  :plans  - Testplaene direkt vom Plan-Server abrufen");
    Console.WriteLine("  :tools  - Tools direkt vom Exec-Server abrufen");
    Console.WriteLine();
    Console.WriteLine("LLM-first Beispiele (ein Aufruf -> automatisch beide Server):");
    Console.WriteLine("  'Welche Testplaene gibt es?'");
    Console.WriteLine("  'Zeige mir den google-news Plan'");
    Console.WriteLine("  'Fuehre den google-news Test aus'");
    Console.WriteLine("  'Liste die Plaene, lies google-news und fuehre ihn aus'");
}
