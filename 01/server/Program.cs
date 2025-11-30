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

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000";
builder.WebHost.UseUrls(url);
Log($"[Server] Configuring on {url}...");

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly();

var app = builder.Build();

// MCP-Endpunkte bereitstellen (u. a. /sse und /message)
app.MapMcp();

// einfache Info-Route
app.MapGet("/health", () => Results.Ok(new { status = "ok", sse = "/sse" }));

Log($"[Server] Demo 01 - Zeit-Server gestartet");
Log($"[Server] MCP SSE Endpunkt: {url}/sse");
Log($"[Server] Health Check: {url}/health");

app.Run();