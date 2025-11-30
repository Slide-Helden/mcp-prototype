using System.Text;
using System.Text.Json;
using DocServer;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.HttpLogging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

var builder = WebApplication.CreateBuilder(args);

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5200";
builder.WebHost.UseUrls(url);
Log($"[Server] Configuring on {url}...");

// Logging mit Timestamps konfigurieren
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[HH:mm:ss.fff] ";
    options.SingleLine = true;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Services registrieren
builder.Services.AddSingleton<DocumentCatalog>();
builder.Services.AddSingleton<TraceStore>();

// HTTP Logging fuer detaillierte Traces
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders
        | HttpLoggingFields.ResponsePropertiesAndHeaders;
    o.MediaTypeOptions.AddText("application/json");
    o.MediaTypeOptions.AddText("text/event-stream");
    o.RequestBodyLogLimit = 4096;
    o.ResponseBodyLogLimit = 4096;
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly()
    .WithGetPromptHandler((context, cancellation) =>
    {
        var services = context.Services ?? throw new InvalidOperationException("Service provider unavailable.");
        var catalog = services.GetRequiredService<DocumentCatalog>();
        var traceStore = services.GetRequiredService<TraceStore>();
        var name = context.Params?.Name ?? string.Empty;

        traceStore.AddMcpRequest("prompts/get", $"name: {name}");

        static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            var lastSlash = trimmed.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < trimmed.Length - 1)
            {
                trimmed = trimmed[(lastSlash + 1)..];
            }
            return trimmed;
        }

        var docId = Normalize(name);
        var document = catalog.TryGet(docId);

        if (document is not null)
        {
            traceStore.AddMcpResponse("prompts/get", $"Found document: {document.Title}");

            return ValueTask.FromResult(PromptFallbackExtensions.CreatePromptResult(
                new ChatMessage(ChatRole.System,
                    "Du bist ein erfahrener Koch-Assistent. Du hast Zugriff auf eine Rezeptsammlung. " +
                    "Deine Aufgabe: Lies das Rezept per read_resource docs/document/{id}. " +
                    "Analysiere die Zutaten und die Zubereitungsschritte. " +
                    "Gib hilfreiche Tipps zur Zubereitung und nenne immer das genutzte Rezept als Quelle."),
                new ChatMessage(ChatRole.User,
                    $"Rezept-ID: {document.Id}\n" +
                    $"Gericht: {document.Title}\n" +
                    $"Lies den Volltext des Rezepts und hilf dem Nutzer beim Kochen.")
            ));
        }

        traceStore.AddMcpResponse("prompts/get", $"Error: Unknown prompt '{name}'");
        return ValueTask.FromException<GetPromptResult>(new McpException($"Unknown prompt: '{name}'"));
    });

var app = builder.Build();

// Trace-Middleware: Erfasst alle HTTP-Anfragen und MCP JSON-RPC Nachrichten
app.Use(async (ctx, next) =>
{
    var store = ctx.RequestServices.GetRequiredService<TraceStore>();
    var path = ctx.Request.Path.ToString();

    // Request Body lesen und tracen
    string body = string.Empty;
    if (ctx.Request.ContentLength > 0 && ctx.Request.Body.CanRead)
    {
        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
        body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
    }

    // HTTP Request tracen
    store.AddRequest(ctx.Request.Method, path + ctx.Request.QueryString, body);

    // MCP JSON-RPC spezifisch parsen
    if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('{'))
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString() ?? "unknown";
                var paramsStr = doc.RootElement.TryGetProperty("params", out var paramsProp)
                    ? paramsProp.ToString()
                    : null;
                store.AddMcpRequest(method, paramsStr);
            }
        }
        catch { /* nicht-JSON oder parse fehler - ignorieren */ }
    }

    await next.Invoke();

    // Response tracen
    store.AddResponse(ctx.Response.StatusCode, ctx.Response.ContentType);
});

app.UseHttpLogging();

app.MapGet("/health", (DocumentCatalog catalog, TraceStore traceStore) =>
{
    traceStore.Add(TraceDirection.Internal, "SYS", "Health check aufgerufen", null);
    return Results.Ok(new
    {
        status = "ok",
        documents = catalog.List().Count,
        sse = "/sse",
        trace = "/trace/logs",
        traceMarkdown = "/trace/logs/markdown"
    });
});

app.MapGet("/documents", (DocumentCatalog catalog) =>
    Results.Ok(catalog.List().Select(doc => doc.ToSummaryDto())));

// Direkter HTTP-Zugang zu Traces (ohne MCP)
app.MapGet("/trace/logs", (TraceStore store) => Results.Text(store.Dump(), "text/plain"));
app.MapGet("/trace/logs/markdown", (TraceStore store) => Results.Text(store.DumpMarkdown(), "text/markdown"));

app.MapMcp();

Log("[Server] Demo 04 - Rezept-Server mit Live Trace gestartet");
Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Demo 04: Rezept-Server mit MCP Communication Trace                  ║");
Console.WriteLine("╠════════════════════════════════════════════════════════════════════════════════╣");
Console.WriteLine($"║ Endpunkte:                                                                     ║");
Console.WriteLine($"║   - MCP SSE:        {url}/sse                                  ║");
Console.WriteLine($"║   - Health:         {url}/health                               ║");
Console.WriteLine($"║   - Trace (Text):   {url}/trace/logs                           ║");
Console.WriteLine($"║   - Trace (MD):     {url}/trace/logs/markdown                  ║");
Console.WriteLine("║                                                                                 ║");
Console.WriteLine("║ MCP Resources:                                                                  ║");
Console.WriteLine("║   - trace/logs          - Trace als formatierter Text                          ║");
Console.WriteLine("║   - trace/logs/markdown - Trace als Markdown-Tabelle                           ║");
Console.WriteLine("║                                                                                 ║");
Console.WriteLine("║ MCP Tools:                                                                      ║");
Console.WriteLine("║   - trace.stats   - Statistiken zur Kommunikation                              ║");
Console.WriteLine("║   - trace.clear   - Trace-Markierung setzen                                    ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝");

app.Run();
