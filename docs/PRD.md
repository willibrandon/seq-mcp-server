Below is a **concise Product-Requirements Document (PRD)** for the vendor-neutral **Seq MCP Server** that wraps Seq’s HTTP API with three essential Model Context Protocol tools.
It purposefully excludes every external-provider feature; API keys live in a local file; and complexity is kept in check via the KISS and YAGNI principles.([baeldung.com][1], [techtarget.com][2])

---

## Problem statement

Teams using Seq want to ask natural-language questions—“Show me errors from the last hour” or “Tail the ops workspace”—directly from an AI assistant.  Today, assistants can’t call Seq without bespoke glue code.  A tiny MCP server that speaks Seq solves this gap without dragging in heavyweight secrets or infrastructure.

## Goals

* **Expose three high-value operations**: filter search, SQL query, live tail.
* **Stay vendor-neutral**: no SDKs or vaults from external providers.
* **Run anywhere**: single Docker Compose file (Seq + server).([docs.datalust.co][3])
* **Keep code <1 k LOC**, easy for one dev to maintain.
* **Ship in < 1 week** using .NET 8 and the official `Seq.Api` library.([baeldung.com][1])

## Non-goals

* Dashboard/retention CRUD, hot-swap telemetry, multi-tenant secret discovery, or any scaling optimizations.

---

## Personas & scenarios

| Persona                | Scenario                                                                     | Success criterion                     |
| ---------------------- | ---------------------------------------------------------------------------- | ------------------------------------- |
| *Ops Engineer*         | “Tail errors live” from ChatGPT while debugging an outage.                   | Events appear with < 1 s delay.       |
| *Site Reliability Bot* | Runs nightly SQL query (“top 10 slow routes”) via MCP.                       | Receives JSON table in < 500 ms.      |
| *Developer*            | Rotates the Seq API key in `secrets.json`; server picks it up automatically. | Next call authenticates with new key. |

---

## Functional requirements

1. **MCP tools**

   * `seq_search(filter, limit=100, workspace)` → list of `EventEntity`.
   * `seq_query(sql, workspace)` → `QueryResult`.
   * `seq_stream(filter?, afterId?, workspace)` → async stream of `EventEntity`.
   * `signal_list(workspace)` (read-only helper).
     All are thin wrappers around the Seq endpoints described in the HTTP-API docs.([docs.datalust.co][4])

2. **Credential store**

   * `secrets.json` maps workspace→API key.
   * Hot-reload on file change (`FileSystemWatcher`).([devblogs.microsoft.com][5])
   * Read-only mount; never logged.

3. **Connection factory** – creates `SeqConnection` per workspace using the key.

4. **Live streaming**

   * Uses `Events.SubscribeAsync()` to avoid the 10 000-event queue limit.([github.com][6])

5. **Observability**

   * Console logs.
   * Prometheus metrics via `prometheus-net` ASP.NET exporter.([github.com][7])

6. **Health endpoint** `/healthz`

   * Returns 200 if a quick `Signals.ListAsync()` call succeeds; 503 otherwise.

7. **Version guard**

   * Config lists `MinSeqVersion`, `MaxSeqVersion`; server logs warning outside range.

---

## Non-functional requirements

| Quality          | Target                                                   |
| ---------------- | -------------------------------------------------------- |
| **Latency**      | ≤ 50 ms p99 overhead for search/query calls.             |
| **Throughput**   | 500 concurrent streams, verified via load test.          |
| **Availability** | 99.9 % (restart server to apply config).                 |
| **Security**     | `secrets.json` read-only; never written or echoed.       |
| **Simplicity**   | Core code ≤ 1 000 lines; no external provider libraries. |

---

## Milestones & timeline (5 work-day plan)

| Day | Deliverable & test                                                  | Owner |
| --- | ------------------------------------------------------------------- | ----- |
| 1   | Skeleton solution `SeqMcpServer.sln`; compile                       | Dev   |
| 1   | File-based `ICredentialStore` + hot-reload unit test                | Dev   |
| 2   | `SeqConnectionFactory` + smoke test (search)                        | Dev   |
| 2   | Implement `seq_search` & `seq_query`; Docker Compose up             | Dev   |
| 3   | Implement `seq_stream`; 30-min load test                            | Dev   |
| 3   | `/healthz` endpoint; break Seq → expect 503                         | Dev   |
| 4   | Prometheus `/metrics`; verify scrape                                | Dev   |
| 4   | Add hard-coded version guard; manual test                           | Dev   |
| 5   | README quick-start, licence, push to GitHub as **`seq-mcp-server`** | Dev   |

Total effort: ≈ 35–40 focused hours.

---

## Success metrics

* MVP deployed and answering AI queries within 1 week.
* Latency target met under load test.
* Hot-reload of `secrets.json` proven by unit test.
* < 800 LOC core (excluding generated code).
* Repository accrues at least 25 GitHub stars in the first month—benchmark comparable to other Seq utilities (e.g., `seq-api` at 80 stars).([github.com][6])

---

## Open questions

1. Do we need workspace isolation beyond API key separation?
2. Will we support SQL result pagination in v1, or keep limit=10 000 default?
3. Should `/metrics` be optional behind a flag?

---

### Key references

* **Seq SubscribeAsync** queue behavior([github.com][6])
* **Seq.Api** client source([github.com][6])
* **HTTP-API usage** examples([docs.datalust.co][4])
* **System.Text.Json** unmapped-member skip ([learn.microsoft.com][8])
* **FileSystemWatcher** hot reload pattern ([stackoverflow.com][9])
* **prometheus-net** exporter intro([github.com][7])
* **KISS principle** overview([baeldung.com][1])
* **YAGNI principle** definition([techtarget.com][2])
* Docker guide for Seq image([docs.datalust.co][3])
* .NET 8 JSON updates (background for unmapped handling)([devblogs.microsoft.com][5])

This PRD captures only the essentials—no external infrastructure, no hypothetical scaling work—so development stays fast and focused.

[1]: https://www.baeldung.com/cs/kiss-software-design-principle?utm_source=chatgpt.com "KISS Software Design Principle | Baeldung on Computer Science"
[2]: https://www.techtarget.com/whatis/definition/You-arent-gonna-need-it?utm_source=chatgpt.com "What is YAGNI principle (You Aren't Gonna Need It)? - TechTarget"
[3]: https://docs.datalust.co/docs/getting-started-with-docker?utm_source=chatgpt.com "Getting Started with Docker - Seq Documentation"
[4]: https://docs.datalust.co/docs/using-the-http-api?utm_source=chatgpt.com "Using the HTTP API - Seq Documentation"
[5]: https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-8/?utm_source=chatgpt.com "What's new in System.Text.Json in .NET 8 - Microsoft Developer Blogs"
[6]: https://github.com/datalust/seq-api?utm_source=chatgpt.com "datalust/seq-api: HTTP API client for Seq - GitHub"
[7]: https://github.com/prometheus-net/prometheus-net?utm_source=chatgpt.com "NET library to instrument your code with Prometheus metrics - GitHub"
[8]: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members?utm_source=chatgpt.com "Handle unmapped members during deserialization - .NET"
[9]: https://stackoverflow.com/questions/9804401/how-to-use-filesystemwatcher-to-change-cache-data?utm_source=chatgpt.com "how to use FileSystemWatcher to change cache data?"
