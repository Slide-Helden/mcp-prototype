using Microsoft.AspNetCore.HttpLogging;
using TestPlanExecutor;

// ---------- Logging konfigurieren ----------
var logLevel = LogLevel.Debug; // hier Loglevel aendern (Trace fuer JSON-RPC Details)

var builder = WebApplication.CreateBuilder(args);

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5001";
builder.WebHost.UseUrls(url);

// Logging mit Timestamps und konfigurierbarem Level
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[HH:mm:ss.fff] ";
    options.SingleLine = true;
    options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
});
builder.Logging.SetMinimumLevel(logLevel);
builder.Logging.AddFilter("ModelContextProtocol", logLevel);

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
    note = "Exec-Server fuer Testplan-Ausfuehrung"
}));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Demo 07 - Exec-Server (nur Tools) gestartet");
logger.LogInformation("MCP SSE Endpunkt: {Url}/sse", url);

app.Run();
