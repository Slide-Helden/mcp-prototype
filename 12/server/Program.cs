using OpsServer;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5600");

builder.Services.AddSingleton<OpsState>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();

var app = builder.Build();

app.MapMcp();

app.MapGet("/health", (OpsState state) => Results.Ok(new
{
    status = "ok",
    services = state.ListServices().Count,
    sse = "/sse"
}));

app.Run();
