using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SeqMcpServer.Services;

// Load environment variables from .env file if it exists
// Try multiple strategies to find the .env file
string? envPath = null;

// Strategy 1: Check current directory
if (File.Exists(".env"))
{
    envPath = Path.GetFullPath(".env");
}
// Strategy 2: Walk up from current directory to find .env
else
{
    var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (currentDir != null && envPath == null)
    {
        var possibleEnvPath = Path.Combine(currentDir.FullName, ".env");
        if (File.Exists(possibleEnvPath))
        {
            envPath = possibleEnvPath;
            break;
        }
        
        // Stop if we find a .sln file (we've reached the solution root)
        if (currentDir.GetFiles("*.sln").Any())
        {
            break;
        }
        
        currentDir = currentDir.Parent;
    }
}

// Strategy 3: Check common locations relative to the executable
if (envPath == null)
{
    var baseDir = AppContext.BaseDirectory;
    var possiblePaths = new[]
    {
        Path.Combine(baseDir, ".env"),
        Path.Combine(baseDir, "..", "..", "..", ".env"),
        Path.Combine(baseDir, "..", "..", "..", "..", ".env"),
        Path.Combine(baseDir, "..", "..", "..", "..", "..", ".env")
    };
    
    foreach (var path in possiblePaths)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            envPath = fullPath;
            break;
        }
    }
}

// Load the .env file if found
if (envPath != null && File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") && line.Contains('='))
        {
            var parts = line.Split('=', 2);
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

// Create host builder for the MCP server
var builder = Host.CreateApplicationBuilder(args);

// Clear all default logging providers to prevent console output
// MCP servers must not write to stdout/stderr as it interferes with JSON-RPC communication
builder.Logging.ClearProviders();
// Register services
builder.Services.AddSingleton<ICredentialStore, EnvironmentCredentialStore>();
builder.Services.AddSingleton<SeqConnectionFactory>();

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
