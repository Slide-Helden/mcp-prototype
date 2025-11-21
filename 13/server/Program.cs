using System.Text;
using Microsoft.AspNetCore.HttpLogging;
using TraceServer;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5700");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddSingleton<TraceStore>();

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
    .WithResourcesFromAssembly();

var app = builder.Build();

// Lightweight trace middleware (logs HTTP + short body into TraceStore)
app.Use(async (ctx, next) =>
{
    var store = ctx.RequestServices.GetRequiredService<TraceStore>();
    var now = DateTimeOffset.UtcNow;

    string body = string.Empty;
    if (ctx.Request.ContentLength > 0 && ctx.Request.Body.CanRead)
    {
        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
        body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
        if (body.Length > 800) body = body[..800] + "...(truncated)";
    }

    store.Add($"[{now:u}] HTTP {ctx.Request.Method} {ctx.Request.Path} {ctx.Request.QueryString} Body={body}");
    await next.Invoke();
    store.Add($"[{DateTimeOffset.UtcNow:u}] --> {ctx.Response.StatusCode} ({ctx.Response.ContentType})");
});

app.UseHttpLogging();

app.MapMcp();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    sse = "/sse",
    trace = "/trace/logs"
}));

app.MapGet("/trace/logs", (TraceStore store) => Results.Text(store.Dump(), "text/plain"));

app.Run();
