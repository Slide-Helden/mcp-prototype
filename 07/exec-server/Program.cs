using Microsoft.AspNetCore.HttpLogging;
using TestPlanExecutor;

static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

var builder = WebApplication.CreateBuilder(args);

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5001";
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

builder.Services.AddHttpClient<TestPlanRunner>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<TestPlanRunner>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders
        | HttpLoggingFields.ResponsePropertiesAndHeaders;
    o.MediaTypeOptions.AddText("application/json");
    o.MediaTypeOptions.AddText("text/event-stream");
});

var app = builder.Build();

app.UseHttpLogging();

app.MapMcp();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    plans = new[] { "google-news" },
    sse = "/sse",
    note = "Dies ist der Ausfuehrungs-Server fuer Testplaene."
}));

Log($"[Server] Demo 07 - Exec-Server (Ausfuehrung) gestartet");
Log($"[Server] MCP SSE Endpunkt: {url}/sse");
Log($"[Server] Health Check: {url}/health");

app.Run();
