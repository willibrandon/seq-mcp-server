using SeqMcpServer.Services;
using Prometheus;
using ModelContextProtocol.Server;

// Check if we should run as MCP server (stdio) or web server
if (args.Contains("--mcp") || Environment.GetEnvironmentVariable("MCP_MODE") == "true")
{
    // Run as MCP server with stdio transport
    var mcpBuilder = Host.CreateApplicationBuilder(args);
    
    mcpBuilder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
    mcpBuilder.Services.AddSingleton<SeqConnectionFactory>();
    mcpBuilder.Configuration["CredentialFile"] = "secrets.json";
    
    mcpBuilder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
    
    mcpBuilder.Services.AddLogging(b => b.AddSimpleConsole());
    
    await mcpBuilder.Build().RunAsync();
}
else
{
    // Run as web application
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
    builder.Services.AddSingleton<SeqConnectionFactory>();
    builder.Configuration["CredentialFile"] = "secrets.json";

    // Add logging
    builder.Services.AddLogging(b => b.AddSimpleConsole());

    var app = builder.Build();

    // Seq version guard
    var minVer = Version.Parse(builder.Configuration["SeqVersion:Min"] ?? "2024.1");
    var maxVer = Version.Parse(builder.Configuration["SeqVersion:Max"] ?? "2025.1");

    app.Lifetime.ApplicationStarted.Register(async () =>
    {
        try
        {
            var conn = app.Services.GetRequiredService<SeqConnectionFactory>().Create();
            var root = await conn.Client.GetRootAsync();
            var ver = Version.Parse(root.Version);
            app.Logger.LogInformation("Connected to Seq version {Version}", ver);
            if (ver < minVer || ver > maxVer)
                app.Logger.LogWarning("Seq version {Version} outside supported range {MinVersion}-{MaxVersion}", ver, minVer, maxVer);
        }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Unable to retrieve Seq version"); }
    });

    // Add Prometheus metrics endpoint
    app.MapMetrics();

    app.MapGet("/healthz", async (SeqConnectionFactory fac) =>
    {
        try
        {
            await fac.Create().Signals.ListAsync(shared: true);
            return Results.Ok(new { status = "ok" });
        }
        catch
        {
            return Results.StatusCode(503);
        }
    });

    app.Run();
}

public partial class Program { }
