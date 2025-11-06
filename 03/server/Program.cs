using OperatorServer;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5400");

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

app.Run();
