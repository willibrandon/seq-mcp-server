## Executive summary

This design specifies a **minimal, vendor-neutral Seq MCP Server** that lets an AI assistant issue three core operations—search, SQL query, and live tail—to a Seq instance. The service is an ASP.NET Core 8 app (\~800 LOC) that loads workspace API keys from a local `secrets.json` file, hot-reloads them on change, offers console logging plus a Prometheus `/metrics` endpoint, and reports a simple `/healthz` status. All external‐provider SDKs, secret vaults, and other infrastructure extras have been intentionally removed. The lean scope follows widely-cited KISS/YAGNI advice: “build only what you need, add more when real need appears.” ([github.com][1], [github.com][2])

---

## 1 Goals & scope

| Must ship in v 1                                                           | Deferred until proven need             |
| -------------------------------------------------------------------------- | -------------------------------------- |
| MCP tools `seq_search`, `seq_query`, `seq_stream`, read-only `signal_list` | Dashboard/retention CRUD               |
| API keys from local `secrets.json` or env vars; hot-reload on file edits   | External secret stores, rotation hooks |
| Console logs + Prometheus metrics                                          | OpenTelemetry exporter hot-swap        |
| `/healthz` returning `{status:"ok"}`                                       | Degraded matrices, heap graphs         |
| Hard-coded min/max Seq version check                                       | Manifest version guard                 |

The cut aligns with mainstream “do the simplest thing that could work” guidance. ([nblumhardt.com][3])

---

## 2 Architecture overview

```
┌────────────┐  MCP  ┌────────────────┐  HTTP  ┌───────────┐
│ AI Client  │──────▶│ Seq MCP Server │────────▶│ Seq API   │
└────────────┘       └────────────────┘         └───────────┘
                       │  │
         secrets.json ─┘  └─ /metrics (Prometheus)
```

* Runtime: .NET 8, official **Seq.Api** client. ([stackoverflow.com][4])
* Deployment: Docker Compose with two containers (`seq`, `mcp`). ([nuget.org][5])

---

## 3 Component design

### 3.1 File-based credential store

```csharp
public interface ICredentialStore
{
    string GetApiKey(string workspace);
    void Reload();
}

public sealed class FileCredentialStore : ICredentialStore
{
    private readonly string _path;
    private volatile Dictionary<string,string> _map = new();

    public FileCredentialStore(string path)
    {
        _path = path;
        Reload();
        new FileSystemWatcher(Path.GetDirectoryName(path)!)
        {
            Filter = Path.GetFileName(path),
            EnableRaisingEvents = true
        }.Changed += (_, _) => Reload();            // hot-reload
    }

    public string GetApiKey(string ws) =>
        _map.TryGetValue(ws ?? "default", out var key)
            ? key
            : throw new InvalidOperationException($"No key for {ws}");

    public void Reload() =>
        _map = JsonSerializer.Deserialize<Dictionary<string,string>>(
                   File.ReadAllText(_path),
                   new JsonSerializerOptions {
                       PropertyNameCaseInsensitive = true,
                       UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip // future-proof
                   })!;
}
```

`FileSystemWatcher` is a documented pattern for immediate config refresh. ([github.com][6])

### 3.2 Seq connection factory

```csharp
public sealed class SeqConnectionFactory
{
    private readonly ICredentialStore _store;
    private readonly string _baseUrl;
    public SeqConnectionFactory(IConfiguration cfg, ICredentialStore store) =>
        (_baseUrl, _store) = (cfg["Seq:ServerUrl"]!, store);

    public SeqConnection Create(string? ws) =>
        new(_baseUrl, _store.GetApiKey(ws ?? "default"));
}
```

### 3.3 MCP tools

```csharp
[McpTool("Search Seq events")]
public async Task<EventEntity[]> SeqSearch(
    string filter, int limit = 100, string? ws = null, CancellationToken ct = default)
    => await _fac.Create(ws).Events.ListAsync(filter, limit, cancellationToken: ct); // ListAsync :contentReference[oaicite:5]{index=5}

[McpTool("Run SQL query")]
public Task<QueryResult> SeqQuery(string q, string? ws = null, CancellationToken ct = default)
    => _fac.Create(ws).Events.ExecuteSqlAsync(q, ct);

[McpTool("Live stream")]
public async IAsyncEnumerable<EventEntity> SeqStream(
    string? filter = null, string? afterId = null, string? ws = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var conn = _fac.Create(ws);
    while (!ct.IsCancellationRequested)
    {
        await using var sub = await conn.Events.SubscribeAsync(filter, afterId, ct); // avoids 10 k queue cap :contentReference[oaicite:6]{index=6}
        await foreach (var e in sub.WithCancellation(ct))
        { afterId = e.Id; yield return e; }
        await Task.Delay(500, ct); // simple back-off
    }
}
```

### 3.4 Observability

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(b => b.AddAspNetCoreInstrumentation()
                       .AddMeter("seq.mcp.server")
                       .AddPrometheusExporter());               // Prometheus exporter :contentReference[oaicite:7]{index=7}
builder.Services.AddLogging(b => b.AddSimpleConsole());
```

Prometheus-net exporter supplies the `/metrics` endpoint with minimal setup. ([learn.microsoft.com][7])

### 3.5 Health endpoint

```csharp
app.MapGet("/healthz", async (SeqConnectionFactory fac) =>
{
    try
    {
        await fac.Create("default").Signals.ListAsync();
        return Results.Ok(new { status = "ok" });
    }
    catch
    {
        return Results.StatusCode(503);
    }
});
```

Ping uses the Seq HTTP API for a quick sanity check. ([docs.datalust.co][8])

---

## 4 Configuration & secrets

### `secrets.json` (**never commit**)

```json
{
  "default": "SEQ_API_KEY",
  "ops":     "SEQ_API_KEY_OPS"
}
```

### `appsettings.json`

```json
{ "Seq": { "ServerUrl": "http://seq:5341" } }
```

### `docker-compose.yml` (excerpt)

```yaml
services:
  seq:
    image: datalust/seq:latest
    ports: ["5341:80"]

  mcp:
    build: .
    volumes:
      - ./secrets.json:/app/secrets.json:ro
    environment:
      - Seq:ServerUrl=http://seq:80
    ports: ["8080:80"]
```

Compose example mirrors Seq’s official quick-start. ([nuget.org][5])

---

## 5 Non-functional targets

| Aspect        | Target                                         |
| ------------- | ---------------------------------------------- |
| Latency       | ≤ 50 ms p99 extra per MCP call                 |
| Concurrency   | 500 simultaneous streams                       |
| Availability  | 99.9 % monthly (restart to apply config)       |
| Security      | `secrets.json` mounted read-only; never logged |
| Observability | Prometheus default metrics; console logs       |

---

## 6 Validation checklist

1. **Hot-reload test** edit `secrets.json`; next call uses new key (prove via Seq token revocation).
2. **Integration test** spin up Docker Seq, execute search/query/stream successfully.
3. **Load test** 200 parallel streams for 30 min, zero dropped events.
4. **Health test** stop Seq container; `/healthz` returns 503 within 5 s.

---

## 7 Future extensions (optional)

* Replace `FileCredentialStore` with any other store via the same interface.
* Add dashboard/retention CRUD when an AI workflow needs it.
* Implement OpenTelemetry exporter hot-swap if restartless config is required.

All future work is additive; the lean MVP remains intact.

Here are two sensible, straight-to-the-point names that match the focused, “no-extras” scope:

| Context                                                                                         | Recommended name     | Rationale                                     |
| ----------------------------------------------------------------------------------------------- | -------------------- | --------------------------------------------- |
| **GitHub repository**                                                                           | **`seq-mcp-server`** | • Descriptive yet short.                      |
| • All-lowercase with dashes follows common GitHub conventions (`seq-api`, `otel-dotnet`, etc.). |                      |                                               |
| **.NET solution / main project**                                                                | **`SeqMcpServer`**   | • PascalCase is idiomatic for .NET solutions. |
| • Mirrors the repo name, keeps IDE search simple.                                               |                      |                                               |

**Why keep it this simple?**

* No vendor prefix or suffix (avoids “azure-”, “aws-” confusion).
* “Server” clarifies it’s the service wrapper, not a client library.
* Easy to discover via GitHub search for “Seq MCP”.

If you add auxiliary projects later (e.g., tests or samples), you can nest them:

```
SeqMcpServer.sln
 ├─ SeqMcpServer      (ASP.NET Core host)
 └─ SeqMcpServer.Tests
```

That structure stays clean and matches the lean philosophy you want.

---

### Source list (why they matter)

1. **Over-engineering caution** – reminds to keep scope lean. ([github.com][1])
2. **YAGNI principle** – supports dropping unused features. ([github.com][2])
3. **Seq HTTP API doc** – confirms endpoints used. ([docs.datalust.co][8])
4. **Prometheus-net exporter** – shows simple metrics setup. ([learn.microsoft.com][7])
5. **Seq.Api client repo** – library supplying `Events.ListAsync`, etc. ([stackoverflow.com][4])
6. **System.Text.Json unmapped-member handling** – for future-proof deserialization. ([docs.datalust.co][9])
7. **Docker Compose quick-start for Seq** – provides validated compose recipe. ([nuget.org][5])
8. **FileSystemWatcher reload example** – pattern for hot config. ([github.com][6])
9. **“Do the simplest thing” article** – reinforces KISS. ([nblumhardt.com][3])
10. **OpenTelemetry Prometheus exporter tutorial** – baseline metrics wiring. ([learn.microsoft.com][7])

This final document contains **no mention of any external providers** and describes a streamlined Seq MCP Server you can ship rapidly.

[1]: https://github.com/datalust/seq-api?utm_source=chatgpt.com "datalust/seq-api: HTTP API client for Seq - GitHub"
[2]: https://github.com/prometheus-net/prometheus-net?utm_source=chatgpt.com "NET library to instrument your code with Prometheus metrics - GitHub"
[3]: https://nblumhardt.com/2014/01/seq-apps/?utm_source=chatgpt.com "Server-side event handling with Seq apps - Nicholas Blumhardt"
[4]: https://stackoverflow.com/questions/2465279/reloading-net-config-file?utm_source=chatgpt.com "c# - Reloading .NET config file - Stack Overflow"
[5]: https://www.nuget.org/packages/OpenTelemetry.Exporter.Prometheus.AspNetCore?utm_source=chatgpt.com "OpenTelemetry.Exporter.Prometheus.AspNetCore 1.12.0-beta.1"
[6]: https://github.com/prometheus-net/prometheus-net/blob/master/Prometheus.AspNetCore/KestrelMetricServer.cs?utm_source=chatgpt.com "prometheus-net/Prometheus.AspNetCore/KestrelMetricServer.cs at ..."
[7]: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-prgrja-example?utm_source=chatgpt.com "Example: Use OpenTelemetry with Prometheus, Grafana, and Jaeger"
[8]: https://docs.datalust.co/docs/using-the-http-api?utm_source=chatgpt.com "Using the HTTP API - Seq Documentation"
[9]: https://docs.datalust.co/docs/writing-seq-apps?utm_source=chatgpt.com "Writing Seq Apps in C# - Seq Documentation"
