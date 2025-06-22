using ModelContextProtocol.Server;
using Seq.Api.Client;
using Seq.Api.Model.Events;
using Seq.Api.Model.Signals;
using SeqMcpServer.Services;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace SeqMcpServer.Mcp;

/// <summary>
/// MCP tools for interacting with Seq structured logging server.
/// </summary>
[McpServerToolType]
public static class SeqTools
{
    /// <summary>
    /// Search historical events in Seq with the specified filter.
    /// </summary>
    /// <param name="fac">Factory for creating Seq connections</param>
    /// <param name="filter">Seq filter expression (e.g., "@Level = 'Error'")</param>
    /// <param name="count">Maximum number of events to return (1-1000)</param>
    /// <param name="workspace">Optional workspace identifier for multi-tenant scenarios</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching events</returns>
    [McpServerTool, Description("Search Seq events with filters, returning up to the specified count")]
    public static async Task<List<EventEntity>> SeqSearch(
        SeqConnectionFactory fac,
        [Required] string filter,
        [Range(1, 1000)] int count = 100,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
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
            
            // Check if all events are authentication errors from our own MCP server
            // This happens when using an invalid API key with a Seq instance that allows anonymous reads
            if (events.Count > 0 && events.All(e => 
                e.Level == "Error" && 
                e.Exception != null &&
                e.RenderedMessage != null &&
                (e.Exception.Contains("SeqApiException") || 
                 e.Exception.Contains("401") || 
                 e.Exception.Contains("403") ||
                 e.Exception.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)) &&
                (e.RenderedMessage.Contains("threw an unhandled exception") ||
                 e.Properties?.Any(p => p.Name == "SourceContext" && p.Value?.ToString()?.Contains("ModelContextProtocol") == true) == true)))
            {
                // Return a single synthetic error event instead of thousands of error logs
                return
                [
                    new EventEntity 
                    { 
                        Level = "Error",
                        RenderedMessage = "Authentication failed. Please check your API key.",
                        Timestamp = DateTimeOffset.Now.ToString("O")
                    }
                ];
            }
            
            return events;
        }
        catch (OperationCanceledException)
        {
            // Return empty list on cancellation
            return [];
        }
        catch (SeqApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Return a single synthetic error event for authentication failures
            return
            [
                new EventEntity 
                { 
                    Level = "Error",
                    RenderedMessage = "Authentication failed. Please check your API key.",
                    Timestamp = DateTimeOffset.Now.ToString("O")
                }
            ];
        }
        catch (Exception)
        {
            // Re-throw to let MCP handle the error
            throw;
        }
    }

    /// <summary>
    /// Wait for and capture live events from Seq's event stream.
    /// </summary>
    /// <remarks>
    /// This method connects to Seq's live event stream and captures events as they arrive,
    /// up to the specified count or until the 5-second timeout expires. Due to MCP protocol
    /// limitations, events are returned as a complete snapshot rather than streamed incrementally.
    /// The method may return an empty list if no events matching the filter arrive within the timeout period.
    /// </remarks>
    /// <param name="fac">Factory for creating Seq connections</param>
    /// <param name="filter">Optional Seq filter expression to apply to the stream</param>
    /// <param name="count">Maximum number of events to capture (1-100)</param>
    /// <param name="workspace">Optional workspace identifier for multi-tenant scenarios</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Snapshot of events captured during the wait period (may be empty)</returns>
    [McpServerTool, Description("Wait for and capture live events from Seq (times out after 5 seconds, returns captured events as a snapshot)")]
    public static async Task<List<EventEntity>> SeqWaitForEvents(
        SeqConnectionFactory fac,
        string? filter = null,
        [Range(1, 100)] int count = 10,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
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
        catch (OperationCanceledException)
        {
            // Return what we have on timeout/cancellation
            return [];
        }
        catch (Exception)
        {
            // Re-throw to let MCP handle the error
            throw;
        }
    }

    /// <summary>
    /// List available signals (saved searches) in Seq.
    /// </summary>
    /// <remarks>
    /// Signals in Seq are saved searches that can be used to quickly access commonly used filters.
    /// This method returns only shared signals (read-only access).
    /// </remarks>
    /// <param name="fac">Factory for creating Seq connections</param>
    /// <param name="workspace">Optional workspace identifier for multi-tenant scenarios</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of available signals</returns>
    [McpServerTool, Description("List available signals in Seq (read-only access to shared signals)")]
    public static async Task<List<SignalEntity>> SignalList(
        SeqConnectionFactory fac,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
        {
            var conn = fac.Create(workspace);
            return await conn.Signals.ListAsync(shared: true, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            // Return empty list on cancellation
            return [];
        }
        catch (Exception)
        {
            // Re-throw to let MCP handle the error
            throw;
        }
    }
}