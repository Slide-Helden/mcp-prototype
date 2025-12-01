using Microsoft.AspNetCore.HttpLogging;
using TestPlanServer;

// ---------- Logging konfigurieren ----------
var logLevel = LogLevel.Debug; // hier Loglevel aendern (Trace fuer JSON-RPC Details)

var builder = WebApplication.CreateBuilder(args);

var url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000";
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

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithResourcesFromAssembly();

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
    catalog = "tests/catalog"
}));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Demo 07 - Plan-Server (nur Resources) gestartet");
logger.LogInformation("MCP SSE Endpunkt: {Url}/sse", url);

app.Run();
