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

    [McpServerTool, Description("Stream live events from Seq")]
    public static async IAsyncEnumerable<EventEntity> SeqStream(
        SeqConnectionFactory fac,
        string? filter = null,
        string? workspace = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conn = fac.Create(workspace);
        await foreach (var evt in conn.Events.StreamAsync(
            null,
            null,
            filter ?? ""
        ).WithCancellation(ct))
        {
            yield return evt;
        }
    }

    [McpServerTool, Description("List signals (read-only)")]
    public static async Task<List<SignalEntity>> SignalList(
        SeqConnectionFactory fac,
        string? workspace = null,
        CancellationToken ct = default)
    {
        var conn = fac.Create(workspace);
        return await conn.Signals.ListAsync(cancellationToken: ct);
    }
}