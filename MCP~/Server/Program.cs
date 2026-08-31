using AibtMcpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();

// P6-010: a startup-time snapshot of whatever custom tools are currently registered on the Unity
// side (see CustomTools.cs for why this can't be a compile-time [McpServerToolType] list). Never
// fails startup: an unreachable bridge just means zero custom tools this session.
mcpBuilder.WithTools(CustomToolsLoader.LoadFromBridge());

var app = builder.Build();

await app.RunAsync();
