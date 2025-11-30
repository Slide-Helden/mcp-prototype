using Microsoft.AspNetCore.HttpLogging;
using TestPlanServer;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5800");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

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

app.Run();
