using DocServer;

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
    .WithResourcesFromAssembly();

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
