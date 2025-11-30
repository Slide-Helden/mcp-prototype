// TestPlanOrchestrator - verbindet GitHub-Issue-Server + Plan-Server + Ausfuehrungs-Server

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Text;
using System.Text.Json;

var ghUrl = Environment.GetEnvironmentVariable("MCP_GITHUB_SERVER_URL") ?? "http://localhost:5900/sse";
var planUrl = Environment.GetEnvironmentVariable("MCP_PLAN_SERVER_URL") ?? "http://localhost:5800/sse";
var execUrl = Environment.GetEnvironmentVariable("MCP_EXEC_SERVER_URL") ?? "http://localhost:5850/sse";

Log($"[MCP] Connecting to GitHub-Server: {ghUrl}...");
var ghClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "GitHub Server",
        Endpoint = new Uri(ghUrl)
    }));
Log("[MCP] GitHub-Server connected.");

Log($"[MCP] Connecting to Plan-Server: {planUrl}...");
var planClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Plan Server",
        Endpoint = new Uri(planUrl)
    }));
Log("[MCP] Plan-Server connected.");

Log($"[MCP] Connecting to Executor-Server: {execUrl}...");
var execClient = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Name = "Executor Server",
        Endpoint = new Uri(execUrl)
    }));
Log("[MCP] Executor-Server connected.");

Log($"[MCP] All 3 servers connected:");
Console.WriteLine("Ablauf: Bugs lesen (GitHub) -> Plan nachschlagen (Server 1) -> optional tests.run (Server 2).");

while (true)
{
    PrintMenu();
    Console.Write("> ");
    var choice = Console.ReadLine();

    switch (choice)
    {
        case "1":
            await SearchBugsAndShowPlans(ghClient, planClient);
            break;
        case "2":
            await ListGitHubTools(ghClient);
            break;
        case "3":
            await ShowCatalog(planClient);
            break;
        case "4":
            await ReadPlan(planClient);
            break;
        case "5":
            await RunPlan(execClient);
            break;
        case "6":
            await ListExecutorTools(execClient);
            break;
        case "7":
            return;
        default:
            Console.WriteLine("Unknown choice.");
            break;
    }
}

static async Task SearchBugsAndShowPlans(McpClient ghClient, McpClient planClient)
{
    Console.Write("GitHub-Suchstring (z. B. repo:owner/repo is:issue is:open label:bug): ");
    var query = (Console.ReadLine() ?? "is:issue is:open label:bug").Trim();

    var toolName = await PickIssueSearchToolAsync(ghClient);
    if (toolName is null)
    {
        Console.WriteLine("Kein Issues-Search-Tool auf GitHub-Server gefunden.");
        return;
    }

    Console.WriteLine($"Nutze Tool {toolName} auf GitHub-Server...");
    var args = BuildIssueSearchArgs(query);

    try
    {
        var result = await ghClient.CallToolAsync(toolName, args);
        PrintContent(result.Content.ToAIContents(), $"Bugs (Tool {toolName})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"GitHub-Call fehlgeschlagen: {ex.Message}");
        return;
    }

    var catalog = await planClient.ReadResourceAsync("tests/catalog");
    PrintContent(catalog.Contents.ToAIContents(), "Testplan-Katalog (Server 1)");
}

static async Task ShowCatalog(McpClient planClient)
{
    var res = await planClient.ReadResourceAsync("tests/catalog");
    PrintContent(res.Contents.ToAIContents(), "Plan-Katalog (Server 1)");
}

static async Task ReadPlan(McpClient planClient)
{
    Console.Write("Plan-Slug (z. B. google-news): ");
    var slug = (Console.ReadLine() ?? "google-news").Trim();
    var res = await planClient.ReadResourceAsync($"tests/plan/{slug}");
    PrintContent(res.Contents.ToAIContents(), $"Plan {slug} (Server 1)");
}

static async Task RunPlan(McpClient execClient)
{
    Console.Write("Plan-Name fuer Ausfuehrung (z. B. google-news): ");
    var plan = (Console.ReadLine() ?? "google-news").Trim();
    Console.WriteLine($"Fuehre tests.run auf Executor fuer {plan} aus...");
    var result = await execClient.CallToolAsync("tests.run", new Dictionary<string, object?>
    {
        ["plan"] = plan
    });

    PrintContent(result.Content.ToAIContents(), $"Testergebnis {plan} (Server 2)");
}

static async Task ListExecutorTools(McpClient execClient)
{
    var tools = await execClient.ListToolsAsync();
    Console.WriteLine("Executor-Tools (Server 2):");
    foreach (var t in tools)
    {
        Console.WriteLine($"- {t.Name} : {t.Description}");
    }
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("Multi-Server Testplan Orchestrator");
    Console.WriteLine(" 1) Bugs suchen (GitHub-Server) + Plan-Katalog zeigen");
    Console.WriteLine(" 2) GitHub-Tools listen");
    Console.WriteLine(" 3) Plaene listen (Plan-Server Resource)");
    Console.WriteLine(" 4) Plan lesen (Plan-Server Resource)");
    Console.WriteLine(" 5) Plan ausfuehren (Executor tests.run)");
    Console.WriteLine(" 6) Executor-Tools listen");
    Console.WriteLine(" 7) Exit");
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

static async Task<string?> PickIssueSearchToolAsync(McpClient ghClient)
{
    var tools = await ghClient.ListToolsAsync();
    var preferred = new[]
    {
        "issues.search",
        "github.issues.search",
        "search_issues",
        "searchIssues",
        "issues.list",
        "list_issues"
    };

    var match = tools.FirstOrDefault(t =>
        preferred.Any(p => string.Equals(p, t.Name, StringComparison.OrdinalIgnoreCase)));

    if (match != null) return match.Name;

    return tools.FirstOrDefault(t => t.Name.Contains("issue", StringComparison.OrdinalIgnoreCase))?.Name;
}

static Dictionary<string, object?> BuildIssueSearchArgs(string query)
{
    var args = new Dictionary<string, object?>();
    args["query"] = query;
    args["q"] = query;
    return args;
}

static async Task ListGitHubTools(McpClient ghClient)
{
    var tools = await ghClient.ListToolsAsync();
    Console.WriteLine("GitHub-Tools:");
    foreach (var t in tools)
    {
        Console.WriteLine($"- {t.Name} : {t.Description}");
    }
}

static void Log(string message)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
}
