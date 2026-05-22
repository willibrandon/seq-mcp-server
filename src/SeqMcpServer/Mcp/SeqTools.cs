using ModelContextProtocol.Server;
using Seq.Api.Model.Events;
using Seq.Api.Model.Signals;
using SeqMcpServer.Services;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SeqMcpServer.Mcp;

/// <summary>
/// Result of converting a fuzzy filter expression to a strict Seq filter expression.
/// </summary>
/// <param name="StrictExpression">The strict filter expression (the original input if no conversion was needed).</param>
/// <param name="MatchedAsText">True if Seq interpreted the input as a free-text search rather than a filter expression.</param>
/// <param name="ReasonIfMatchedAsText">Explanation of why the input was interpreted as text, or null when it wasn't.</param>
public sealed record SeqConvertFilterResult(
    [property: JsonPropertyName("strictExpression")] string StrictExpression,
    [property: JsonPropertyName("matchedAsText")] bool MatchedAsText,
    [property: JsonPropertyName("reasonIfMatchedAsText"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReasonIfMatchedAsText);

/// <summary>
/// MCP tools for interacting with Seq structured logging server.
/// </summary>
[McpServerToolType]
public static class SeqTools
{
    /// <summary>
    /// Normalize common filter patterns to Seq's expected format.
    /// </summary>
    private static string NormalizeFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter.Trim() == "*")
        {
            return string.Empty; // Empty string means "all events" in Seq
        }
        return filter;
    }
    /// <summary>
    /// Search historical events in Seq with the specified filter.
    /// </summary>
    /// <param name="fac">Factory for creating Seq connections</param>
    /// <param name="filter">Seq filter expression (e.g., "@Level = 'Error'"). Note: Use fromDateUtc/toDateUtc parameters for date filtering instead of @Timestamp in the filter expression for better performance.</param>
    /// <param name="count">Maximum number of events to return (1-1000)</param>
    /// <param name="signalId">Optional signal ID to filter events (use SignalList to find available signal IDs)</param>
    /// <param name="fromDateUtc">Optional earliest date/time (ISO 8601 format, e.g., '2024-01-01T00:00:00Z'). Use this instead of @Timestamp in filter for better performance.</param>
    /// <param name="toDateUtc">Optional latest date/time (ISO 8601 format, e.g., '2024-01-31T23:59:59Z'). Use this instead of @Timestamp in filter for better performance.</param>
    /// <param name="afterId">Optional event ID to search after (exclusive). Use for pagination - pass the ID of the last event from the previous search to get the next batch.</param>
    /// <param name="timeoutSeconds">Optional timeout in seconds (1-300). If not specified, uses the default cancellation token.</param>
    /// <param name="workspace">Optional workspace identifier for multi-tenant scenarios</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching events</returns>
    [McpServerTool, Description("Search Seq events with filters, date ranges, signals, pagination, and optional timeout. For date filtering, use fromDateUtc/toDateUtc parameters instead of @Timestamp in the filter expression. For pagination, use afterId with the last event ID from previous results.")]
    public static async Task<List<EventEntity>> SeqSearch(
        SeqConnectionFactory fac,
        [Required] string filter,
        [Range(1, 1000)] int count = 100,
        string? signalId = null,
        string? fromDateUtc = null,
        string? toDateUtc = null,
        string? afterId = null,
        [Range(1, 300)] int? timeoutSeconds = null,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
        {
            var conn = fac.Create(workspace);
            var events = new List<EventEntity>();

            // Normalize filter (e.g., "*" becomes empty string for "all events")
            filter = NormalizeFilter(filter);

            // Parse date parameters if provided
            DateTime? fromDate = null;
            DateTime? toDate = null;

            if (!string.IsNullOrEmpty(fromDateUtc))
            {
                if (!DateTime.TryParse(fromDateUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                {
                    throw new ArgumentException($"Invalid fromDateUtc format: {fromDateUtc}. Use ISO 8601 format (e.g., '2024-01-01T00:00:00Z')");
                }
                fromDate = parsed.ToUniversalTime();
            }

            if (!string.IsNullOrEmpty(toDateUtc))
            {
                if (!DateTime.TryParse(toDateUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                {
                    throw new ArgumentException($"Invalid toDateUtc format: {toDateUtc}. Use ISO 8601 format (e.g., '2024-01-31T23:59:59Z')");
                }
                toDate = parsed.ToUniversalTime();
            }

            // Fetch signal entity if signal ID is provided
            SignalEntity? signalEntity = null;
            if (!string.IsNullOrEmpty(signalId))
            {
                signalEntity = await conn.Signals.FindAsync(signalId, cancellationToken: ct);
                if (signalEntity == null)
                {
                    throw new ArgumentException($"Signal with ID '{signalId}' not found. Use SignalList to find available signals.");
                }
            }

            // Create timeout cancellation token if specified
            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource? combinedCts = null;

            try
            {
                if (timeoutSeconds.HasValue)
                {
                    timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds.Value));
                    combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                    ct = combinedCts.Token;
                }

                var hasScanLink = await SeqCapabilities.SupportsScanAsync(conn, ct);

                if (hasScanLink)
                {
                    await foreach (var evt in conn.Events.EnumerateAsync(
                        unsavedSignal: signalEntity,
                        filter: filter,
                        count: count,
                        afterId: afterId,
                        fromDateUtc: fromDate,
                        toDateUtc: toDate,
                        render: true,
                        cancellationToken: ct).WithCancellation(ct))
                    {
                        events.Add(evt);
                    }
                }
                else
                {
                    await foreach (var evt in conn.Events.PagedEnumerateAsync(
                        unsavedSignal: signalEntity,
                        signal: null,
                        filter: filter,
                        count: count,
                        startAtId: null,
                        afterId: afterId,
                        render: true,
                        fromDateUtc: fromDate,
                        toDateUtc: toDate,
                        shortCircuitAfter: null,
                        permalinkId: null,
                        variables: null,
                        background: false,
                        trace: false,
                        cancellationToken: ct).WithCancellation(ct))
                    {
                        events.Add(evt);
                    }
                }

                return events;
            }
            finally
            {
                combinedCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Return empty list on cancellation
            return [];
        }
        catch (Seq.Api.Client.SeqApiException ex) when (ex.Message.Contains("Syntax error"))
        {
            // Provide a more helpful error message for filter syntax errors
            throw new ArgumentException($"Invalid filter expression: {ex.Message}. Use an empty string \"\" for all events, or a valid Seq filter expression like \"@Level = 'Error'\".", ex);
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
                unsavedSignal: null,
                signal: null,
                filter: filter ?? string.Empty,
                cancellationToken: ct).WithCancellation(combinedCts.Token))
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

    /// <summary>
    /// Convert a fuzzy filter expression to a strict Seq filter expression.
    /// </summary>
    /// <remarks>
    /// Seq supports both "fuzzy" filters (like typing in the UI search box) and "strict" filters
    /// (formal filter expressions). This tool converts fuzzy filters to strict ones, helping users
    /// write correct filter expressions. For example, "error" becomes a proper filter expression.
    /// The result includes whether the filter was interpreted as a text search and the reason if so.
    /// </remarks>
    /// <param name="fac">Factory for creating Seq connections</param>
    /// <param name="fuzzyFilter">The fuzzy filter expression to convert (e.g., "error", "timeout")</param>
    /// <param name="workspace">Optional workspace identifier for multi-tenant scenarios</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Conversion result including the strict expression and metadata about the conversion</returns>
    [McpServerTool, Description("Convert a fuzzy filter expression to a strict Seq filter expression. Helps write correct filters for SeqSearch.")]
    public static async Task<SeqConvertFilterResult> SeqConvertFilter(
        SeqConnectionFactory fac,
        [Required] string fuzzyFilter,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
        {
            var conn = fac.Create(workspace);
            var result = await conn.Expressions.ToStrictAsync(fuzzyFilter, cancellationToken: ct);

            if (result == null)
            {
                return new SeqConvertFilterResult(fuzzyFilter, false, null);
            }

            return new SeqConvertFilterResult(
                result.StrictExpression ?? fuzzyFilter,
                result.MatchedAsText,
                result.ReasonIfMatchedAsText);
        }
        catch (Exception)
        {
            // Re-throw to let MCP handle the error. Unlike the list-returning tools in this
            // file, there is no unambiguous "empty" SeqConvertFilterResult value, so cancellation
            // also propagates rather than being swallowed.
            throw;
        }
    }
}
