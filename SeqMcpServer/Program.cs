using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SeqMcpServer.Services;
using ModelContextProtocol.Server;

// Create host builder for the MCP server
var builder = Host.CreateApplicationBuilder(args);

// Clear all default logging providers to prevent console output
// MCP servers must not write to stdout/stderr as it interferes with JSON-RPC communication
builder.Logging.ClearProviders();
// Register services
builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
builder.Services.AddSingleton<SeqConnectionFactory>();
builder.Configuration["CredentialFile"] = "secrets.json";

// Configure MCP server with stdio transport
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Seq version validation on startup (without logging since we can't write to console)
var minVer = Version.Parse(builder.Configuration["SeqVersion:Min"] ?? "2024.1");
var maxVer = Version.Parse(builder.Configuration["SeqVersion:Max"] ?? "2025.1");

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var conn = host.Services.GetRequiredService<SeqConnectionFactory>().Create();
        var root = await conn.Client.GetRootAsync();
        var ver = Version.Parse(root.Version);
        // Version check happens silently - no logging in MCP servers
        if (ver < minVer || ver > maxVer)
        {
            // Could consider failing startup if version is out of range
            // For now, we'll just continue
        }
    }
    catch 
    { 
        // Silently continue - can't log in MCP servers
    }
});

// Run the MCP server
await host.RunAsync();

public partial class Program { }
