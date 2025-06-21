using SeqMcpServer.Services;
using Prometheus;
using ModelContextProtocol.Server;
using Serilog;

// Configure Serilog early for bootstrap
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting SeqMcpServer");

    // Check if we should run as MCP server (stdio) or web server
    if (args.Contains("--mcp") || Environment.GetEnvironmentVariable("MCP_MODE") == "true")
    {
        // Run as MCP server with stdio transport
        var mcpBuilder = Host.CreateApplicationBuilder(args);
        
        // Configure Serilog from configuration  
        mcpBuilder.Services.AddSerilog(configuration => configuration
            .ReadFrom.Configuration(mcpBuilder.Configuration));
        
        mcpBuilder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
        mcpBuilder.Services.AddSingleton<SeqConnectionFactory>();
        mcpBuilder.Configuration["CredentialFile"] = "secrets.json";
        
        mcpBuilder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
        
        await mcpBuilder.Build().RunAsync();
        return;
    }
    
    // Run as web application
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from configuration
    builder.Host.UseSerilog((context, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration));

    builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
    builder.Services.AddSingleton<SeqConnectionFactory>();
    builder.Configuration["CredentialFile"] = "secrets.json";

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
            Log.Information("Connected to Seq {ServerUrl} version {SeqVersion}", 
                conn.Client.ServerUrl, ver);
            if (ver < minVer || ver > maxVer)
                Log.Warning("Seq version {SeqVersion} outside supported range {MinVersion}-{MaxVersion}", 
                    ver, minVer, maxVer);
        }
        catch (Exception ex) 
        { 
            Log.Warning(ex, "Unable to retrieve Seq version from {ServerUrl}", 
                app.Services.GetRequiredService<SeqConnectionFactory>().Create().Client.ServerUrl); 
        }
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
catch (Exception ex)
{
    Log.Fatal(ex, "SeqMcpServer terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program { }
