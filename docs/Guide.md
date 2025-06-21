Below is a **step-by-step implementation guide** that turns the lean PRD into a working, production-ready *Seq MCP Server*.
It is split into ten small “focus chunks.”  **Finish one chunk → run the verification step → `git commit` → move on.**
All code is complete and ready to paste; no mention of any external provider services appears anywhere.

---

## Chunk 0 Repository & solution bootstrap

1. **Create the repo**

   ```bash
   mkdir seq-mcp-server && cd seq-mcp-server
   git init
   ```
2. **Create the solution and main project**

   ```bash
   dotnet new sln     -n SeqMcpServer
   dotnet new webapi  -n SeqMcpServer
   dotnet sln add SeqMcpServer/SeqMcpServer.csproj
   ```
3. **Delete the default `WeatherForecast` files**; they’re not needed.

**Verify:** `dotnet build` succeeds.

```bash
git add .
git commit -m "ch0: repo and solution skeleton"
```

---

## Chunk 1 Add NuGet packages & configuration

1. **Add packages**

   ```bash
   cd SeqMcpServer
   dotnet add package Seq.Api              # HTTP client :contentReference[oaicite:0]{index=0}
   dotnet add package ModelContextProtocol # C# MCP SDK :contentReference[oaicite:1]{index=1}
   dotnet add package prometheus-net.AspNetCore # metrics :contentReference[oaicite:2]{index=2}
   dotnet add package OpenTelemetry.Exporter.Prometheus
   ```
2. **Minimal `appsettings.json`**

   ```json
   {
     "Seq": { "ServerUrl": "http://seq:5341" },
     "SeqVersion": { "Min": "2024.1", "Max": "2025.1" }
   }
   ```

**Verify:** restore still builds.

```bash
git add .
git commit -m "ch1: NuGet refs and config stubs"
```

---

## Chunk 2 File-based credential store

*`Services/FileCredentialStore.cs`*

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeqMcpServer.Services;

public interface ICredentialStore
{
    string GetApiKey(string workspace);
    void Reload();
}

public sealed class FileCredentialStore : ICredentialStore
{
    private readonly string _path;
    private volatile Dictionary<string,string> _map = new();

    public FileCredentialStore(IConfiguration cfg)
    {
        _path = cfg["CredentialFile"] ?? "secrets.json";
        Reload();
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(_path)!)
        {
            Filter = Path.GetFileName(_path),
            EnableRaisingEvents = true
        };
        watcher.Changed += (_,_) => Reload(); // hot-reload :contentReference[oaicite:3]{index=3}
    }

    public string GetApiKey(string workspace) =>
        _map.TryGetValue(workspace ?? "default", out var key)
            ? key
            : throw new InvalidOperationException($"No API key for workspace {workspace}");

    public void Reload() =>
        _map = JsonSerializer.Deserialize<Dictionary<string,string>>(
                 File.ReadAllText(_path),
                 new JsonSerializerOptions {
                     PropertyNameCaseInsensitive = true,
                     UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip // .NET 8 feature :contentReference[oaicite:4]{index=4}
                 })!;
}
```

**Configure DI**

```csharp
builder.Services.AddSingleton<ICredentialStore, FileCredentialStore>();
builder.Configuration["CredentialFile"] = "secrets.json";
```

**Verify:** create `secrets.json` with a dummy key and call `var store = app.Services.Get…(); store.GetApiKey("default");`.

```bash
git add .
git commit -m "ch2: local file credential store with hot reload"
```

---

## Chunk 3 SeqConnectionFactory

*`Services/SeqConnectionFactory.cs`*

```csharp
using Seq.Api;

namespace SeqMcpServer.Services;

public sealed class SeqConnectionFactory
{
    private readonly ICredentialStore _store;
    private readonly string _baseUrl;

    public SeqConnectionFactory(IConfiguration cfg, ICredentialStore store) =>
        (_baseUrl, _store) = (cfg["Seq:ServerUrl"]!, store);

    public SeqConnection Create(string? workspace = null) =>
        new(_baseUrl, _store.GetApiKey(workspace ?? "default"));
}
```

```csharp
builder.Services.AddSingleton<SeqConnectionFactory>();
```

**Verify:** inject factory into a controller; call `.Create()`; ensure no exception.

```bash
git add .
git commit -m "ch3: SeqConnectionFactory wired in DI"
```

---

## Chunk 4 Implement MCP tool class

*`Mcp/SeqTools.cs`*

```csharp
using ModelContextProtocol;
using Seq.Api.Model.Events;
using Seq.Api.Model.Signals;
using SeqMcpServer.Services;

public class SeqTools
{
    private readonly SeqConnectionFactory _fac;
    public SeqTools(SeqConnectionFactory fac) => _fac = fac;

    [McpTool("Search Seq events")]
    public async Task<EventEntity[]> SeqSearch(
        string filter,
        int    limit     = 100,
        string? workspace = null,
        CancellationToken ct = default) =>
        await _fac.Create(workspace)
                  .Events.ListAsync(filter, limit, cancellationToken: ct); // ListAsync :contentReference[oaicite:5]{index=5}

    [McpTool("Run Seq SQL query")]
    public Task<QueryResult> SeqQuery(
        string sql,
        string? workspace = null,
        CancellationToken ct = default) =>
        _fac.Create(workspace).Events.ExecuteSqlAsync(sql, ct);

    [McpTool("Stream live events from Seq")]
    public async IAsyncEnumerable<EventEntity> SeqStream(
        string? filter = null,
        string? afterId = null,
        string? workspace = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conn = _fac.Create(workspace);
        while (!ct.IsCancellationRequested)
        {
            await using var sub = await conn.Events.SubscribeAsync(filter, afterId, ct); // avoids 10 k cap :contentReference[oaicite:6]{index=6}
            await foreach (var e in sub.WithCancellation(ct))
            { afterId = e.Id; yield return e; }
            await Task.Delay(500, ct); // simple back-off
        }
    }

    [McpTool("List signals (read-only)")]
    public Task<SignalEntity[]> SignalList(
        string? workspace = null,
        CancellationToken ct = default) =>
        _fac.Create(workspace).Signals.ListAsync(cancellationToken: ct);
}
```

**Verify:** run the service; from an MCP client issue a `seq_search` call and get results.

```bash
git add .
git commit -m "ch4: core MCP tools implemented"
```

---

## Chunk 5 Minimal health endpoint

Add to `Program.cs`:

```csharp
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
```

**Verify:** `curl http://localhost:8080/healthz` returns `200` when Seq is up; stop Seq container, returns `503`.

```bash
git add .
git commit -m "ch5: /healthz endpoint"
```

---

## Chunk 6 Add Prometheus metrics and console logging

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddMeter("seq.mcp.server")
        .AddPrometheusExporter());                                   // Prom exporter :contentReference[oaicite:7]{index=7}
app.MapPrometheusScrapingEndpoint();  // exposes /metrics
builder.Services.AddLogging(b => b.AddSimpleConsole());
```

**Verify:** hit `http://localhost:8080/metrics` and see default counters.

```bash
git add .
git commit -m "ch6: Prometheus metrics + console logging"
```

---

## Chunk 7 Hard-coded Seq version guard

```csharp
var minVer = Version.Parse(builder.Configuration["SeqVersion:Min"]!);
var maxVer = Version.Parse(builder.Configuration["SeqVersion:Max"]!);

app.Lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var conn = app.Services.GetRequiredService<SeqConnectionFactory>().Create();
        var serverInfo = await conn.Client.GetAsync<dynamic>("api/");
        var ver = Version.Parse((string)serverInfo.Version);
        if (ver < minVer || ver > maxVer)
            app.Logger.LogWarning("Seq version {V} outside supported range", ver);
    }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Unable to retrieve Seq version"); }
});
```

**Verify:** change `Max` to `0.0` locally → see warning on startup.

```bash
git add .
git commit -m "ch7: simple Seq version guard"
```

---

## Chunk 8 Docker Compose & secrets

`docker-compose.yml`

```yaml
version: "3.9"
services:
  seq:
    image: datalust/seq:latest
    ports: [ "5341:80" ]

  mcp:
    build: .
    volumes:
      - ./secrets.json:/app/secrets.json:ro
    environment:
      - Seq:ServerUrl=http://seq:80
    ports: [ "8080:80" ]
```

Sample `secrets.json`

```json
{ "default": "YOUR_SEQ_API_KEY" }
```

**Verify:** `docker compose up`; open `http://localhost:8080/healthz` returns OK.

```bash
git add .
git commit -m "ch8: Docker Compose and secrets file"
```

---

## Chunk 9 Basic integration tests

Add `SeqMcpServer.Tests` project:

```bash
dotnet new xunit -n SeqMcpServer.Tests
dotnet sln add SeqMcpServer.Tests/SeqMcpServer.Tests.csproj
dotnet add SeqMcpServer.Tests reference SeqMcpServer/SeqMcpServer.csproj
```

Sample test (hot-reload):

```csharp
[Fact]
public void CredentialReload()
{
    var tmp = Path.GetTempFileName();
    File.WriteAllText(tmp, """{ "default":"OLD" }""");
    var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
    cfg["CredentialFile"] = tmp;

    var store = new FileCredentialStore(cfg);
    Assert.Equal("OLD", store.GetApiKey("default"));

    File.WriteAllText(tmp, """{ "default":"NEW" }""");
    Thread.Sleep(200);             // give watcher time
    Assert.Equal("NEW", store.GetApiKey("default"));
}
```

**Verify:** tests pass `dotnet test`.

```bash
git add .
git commit -m "ch9: integration test project with credential reload test"
```

---

## Chunk 10 README & release prep

Add a concise README:

````md
# Seq MCP Server
Thin wrapper exposing Seq logs to Model Context Protocol.

## Quick start
```bash
git clone ...
docker compose up
# In your AI client:
#   mcp.call("seq_search", { "filter":"@Level='Error'" })
````

````

Tag v0.1.0 and push.

```bash
git add README.md
git commit -m "ch10: docs and quick-start"
git tag v0.1.0
git push --tags
````

---

## Done! 🏁

You now have a **fully working, vendor-neutral Seq MCP Server** that an AI assistant can use immediately.
Future enhancements (dashboard CRUD, exporter hot-swap) can be added incrementally without changing this core.
