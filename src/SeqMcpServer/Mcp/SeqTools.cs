using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModelContextProtocol.Server;
using Seq.Api.Model.Events;
using Seq.Api.Model.Signals;
using SeqMcpServer.Services;

namespace SeqMcpServer.Mcp;

[McpServerToolType]
public static class SeqTools
{
    [McpServerTool, Description("Search Seq events")]
    public static async Task<List<EventEntity>> SeqSearch(
        SeqConnectionFactory fac,
        string filter,
        int count = 100,
        string? workspace = null,
        CancellationToken ct = default)
    {
        var conn = fac.Create(workspace);
        var events = new List<EventEntity>();
        await foreach (var evt in conn.Events.EnumerateAsync(
            filter: filter,
            count: count,
            render: true
        ).WithCancellation(ct))
        {
            events.Add(evt);
        }
        return events;
    }

    [McpServerTool, Description("Stream live events from Seq (returns first few events)")]
    public static async Task<List<EventEntity>> SeqStream(
        SeqConnectionFactory fac,
        string? filter = null,
        int count = 10,
        string? workspace = null,
        CancellationToken ct = default)
    {
        var conn = fac.Create(workspace);
        var events = new List<EventEntity>();
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        
        await foreach (var evt in conn.Events.StreamAsync(
            null,
            null,
            filter ?? ""
        ).WithCancellation(combinedCts.Token))
        {
            events.Add(evt);
            if (events.Count >= count) break;
        }
        
        return events;
    }

    [McpServerTool, Description("List signals (read-only)")]
    public static async Task<List<SignalEntity>> SignalList(
        SeqConnectionFactory fac,
        string? workspace = null,
        CancellationToken ct = default)
    {
        var conn = fac.Create(workspace);
        return await conn.Signals.ListAsync(shared: true, cancellationToken: ct);
    }
}