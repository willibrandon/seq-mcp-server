using SeqMcpServer.Services;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
builder.Services.AddSingleton<SeqConnectionFactory>();
builder.Configuration["CredentialFile"] = "secrets.json";

// Add logging
builder.Services.AddLogging(b => b.AddSimpleConsole());

var app = builder.Build();

// Seq version guard
var minVer = Version.Parse(builder.Configuration["SeqVersion:Min"]!);
var maxVer = Version.Parse(builder.Configuration["SeqVersion:Max"]!);

app.Lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var conn = app.Services.GetRequiredService<SeqConnectionFactory>().Create();
        var root = await conn.Client.GetRootAsync();
        var serverInfo = await conn.Client.GetAsync<dynamic>(root, "");
        var ver = Version.Parse((string)serverInfo.Version);
        if (ver < minVer || ver > maxVer)
            app.Logger.LogWarning("Seq version {V} outside supported range", ver);
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Unable to retrieve Seq version"); }
});

// Add Prometheus metrics endpoint
app.MapMetrics();

app.MapGet("/healthz", async (SeqConnectionFactory fac) =>
{
    try
    {
        await fac.Create().Signals.ListAsync();
        return Results.Ok(new { status = "ok" });
    }
    catch
    {
        return Results.StatusCode(503);
    }
});

app.Run();
