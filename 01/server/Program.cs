var builder = WebApplication.CreateBuilder(args);

// Feste URL, damit der Client "http://localhost:5000/sse" nutzen kann
builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000");

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


app.Run();
