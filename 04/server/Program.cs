using System;
using System.Threading.Tasks;
using DocServer;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

// Standard-Port fuer die Demo (http://localhost:5200)
builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5200");

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

app.Run();
