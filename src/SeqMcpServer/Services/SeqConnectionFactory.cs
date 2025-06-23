using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Seq.Api;
using Seq.Api.Client;
using System.Net;

namespace SeqMcpServer.Services;

public sealed class SeqConnectionFactory
{
    private readonly ICredentialStore _store;
    private readonly string _baseUrl;
    private readonly ILogger<SeqConnectionFactory>? _logger;

    public SeqConnectionFactory(
        IConfiguration cfg,
        ICredentialStore store,
        ILogger<SeqConnectionFactory>? logger = null)
    {
        // Prefer environment variable, fall back to configuration
        _baseUrl = Environment.GetEnvironmentVariable("SEQ_SERVER_URL")
                   ?? cfg["Seq:ServerUrl"]
                   ?? "http://localhost:5341";

        _store = store;
        _logger = logger;
        _logger?.LogInformation("SeqConnectionFactory initialised with URL: {Url}", _baseUrl);
    }

    public async Task<SeqConnection> CreateAsync(string? workspace = null)
    {
        var apiKey = _store.GetApiKey(workspace ?? "default");
        _logger?.LogInformation(
            "Creating Seq connection to {Url} with API key: {Key}",
            _baseUrl,
            apiKey[..Math.Min(5, apiKey.Length)] + "...");

        var conn = new SeqConnection(_baseUrl, apiKey);

        try
        {
            // Cheap auth probe – /api is public; EventsScan appears only with Read permission
            var root = await conn.Client.GetRootAsync();
            if (!root.Links.ContainsKey("EventsScan"))
                throw new InvalidOperationException(
                    "Authentication failed: API key is missing, invalid, or lacks the Read permission.");
        }
        catch (SeqApiException ex) when (
                   ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Authentication failed: 401/403 – invalid API key.", ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to validate Seq connection");
            throw;
        }

        return conn;
    }
}
