using System;
using System.Threading.Tasks;
using DocServer;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

var builder = WebApplication.CreateBuilder(args);

// Logging mit Timestamps konfigurieren
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[HH:mm:ss.fff] ";
    options.SingleLine = true;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5100";
builder.WebHost.UseUrls(url);
Log($"[Server] Configuring on {url}...");

builder.Services.AddSingleton<DocumentCatalog>();

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
        var name = context.Params?.Name ?? string.Empty;

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
            return ValueTask.FromResult(PromptFallbackExtensions.CreatePromptResult(
                new ChatMessage(ChatRole.System,
                    "Du arbeitest mit einem Dokumentenkatalog. Du hast diesen Prompt ueber einen Dokument-Namen aufgerufen. " +
                    "Deine Aufgabe: lies das Dokument per read_resource docs/document/{id}, ueberpruefe Angaben ggf. ueber docs.summary/{id} " +
                    "und fasse anschliessend die Inhalte kurz zusammen. Gib immer die Quelle an."),
                new ChatMessage(ChatRole.User,
                    $"Dokument-ID: {document.Id}\n" +
                    $"Titel: {document.Title}\n" +
                    $"Nutze falls noetig docs.search fuer Kontext, lies dann den Volltext und erstelle eine Antwort."))
            );
        }

        return ValueTask.FromException<GetPromptResult>(new McpException($"Unknown prompt: '{name}'"));
    });

var app = builder.Build();

app.MapGet("/health", (DocumentCatalog catalog) => Results.Ok(new
{
    status = "ok",
    documents = catalog.List().Count,
    sse = "/sse"
}));

app.MapGet("/documents", (DocumentCatalog catalog) =>
    Results.Ok(catalog.List().Select(doc => doc.ToSummaryDto())));

app.MapMcp();

Log($"[Server] Demo 02 - Dokumenten-Server gestartet");
Log($"[Server] MCP SSE Endpunkt: {url}/sse");
Log($"[Server] Health Check: {url}/health");

app.Run();
