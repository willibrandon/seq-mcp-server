using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Seq.Api;
using Seq.Api.Client;
using Seq.Api.Model.Events;
using SeqMcpServer.Services;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;

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
    public static async Task<CallToolResult> SeqSearch(
        SeqConnectionFactory factory,
        [Required] string filter,
        [Range(1, 1000)] int count = 100,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
        {
            SeqConnection conn;
            try
            {
                conn = await factory.CreateAsync(workspace);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Authentication failed"))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = ex.Message }
                    }
                };
            }
            
            var events = new List<EventEntity>();
            await foreach (var evt in conn.Events.EnumerateAsync(
                    filter: filter,
                    count: count,
                    render: true
                ).WithCancellation(ct))
            {
                events.Add(evt);
            }

            return new CallToolResult
            {
                Content = events.Select(e => new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(e)
                }).ToList<ContentBlock>()
            };
        }
        catch (OperationCanceledException)
        {
            // Return empty list on cancellation
            return new CallToolResult
            {
                Content = new List<ContentBlock>()
            };
        }
        catch (SeqApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = "Authentication failed: 401 Unauthorized - Invalid API key" }
                }
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("The requested link") && ex.Message.Contains("isn't available"))
        {
            // This happens when Seq returns an error response due to invalid API key
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = "Authentication failed: 401 Unauthorized - Invalid API key" }
                }
            };
        }
        catch (Exception ex)
        {   
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = $"Error: {ex.Message}" }
                }
            };
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
    public static async Task<CallToolResult> SeqWaitForEvents(
        SeqConnectionFactory factory,
        string? filter = null,
        [Range(1, 100)] int count = 10,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
        {
            SeqConnection conn;
            try
            {
                conn = await factory.CreateAsync(workspace);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Authentication failed"))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = ex.Message }
                    }
                };
            }
            
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
            
            return new CallToolResult
            {
                Content = events.Select(e => new TextContentBlock 
                { 
                    Text = JsonSerializer.Serialize(e) 
                }).ToList<ContentBlock>()
            };
        }
        catch (OperationCanceledException)
        {
            // Return what we have on timeout/cancellation
            return new CallToolResult
            {
                Content = new List<ContentBlock>()
            };
        }
        catch (SeqApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = "Authentication failed: 401 Unauthorized - Invalid API key" }
                }
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = $"Error: {ex.Message}" }
                }
            };
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
    public static async Task<CallToolResult> SignalList(
        SeqConnectionFactory factory,
        string? workspace = null,
        CancellationToken ct = default)
    {
        try
        {
            SeqConnection conn;
            try
            {
                conn = await factory.CreateAsync(workspace);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Authentication failed"))
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = ex.Message }
                    }
                };
            }
            
            var signals = await conn.Signals.ListAsync(shared: true, cancellationToken: ct);
            
            return new CallToolResult
            {
                Content = signals.Select(s => new TextContentBlock 
                { 
                    Text = JsonSerializer.Serialize(s) 
                }).ToList<ContentBlock>()
            };
        }
        catch (OperationCanceledException)
        {
            // Return empty list on cancellation
            return new CallToolResult
            {
                Content = new List<ContentBlock>()
            };
        }
        catch (SeqApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = "Authentication failed: 401 Unauthorized - Invalid API key" }
                }
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = $"Error: {ex.Message}" }
                }
            };
        }
    }
}