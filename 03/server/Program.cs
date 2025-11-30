using OperatorServer;

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

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5400";
builder.WebHost.UseUrls(url);
Log($"[Server] Configuring on {url}...");

builder.Services.AddSingleton<DocumentCatalog>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();

var app = builder.Build();

app.MapMcp();

app.MapGet("/health", (DocumentCatalog catalog) => Results.Ok(new
{
    status = "ok",
    documents = catalog.List().Count,
    sse = "/sse"
}));

Log($"[Server] Demo 03 - Orchestrator-Server gestartet");
Log($"[Server] MCP SSE Endpunkt: {url}/sse");
Log($"[Server] Health Check: {url}/health");

app.Run();
