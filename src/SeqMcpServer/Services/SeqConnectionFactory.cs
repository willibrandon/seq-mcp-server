using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Seq.Api;

namespace SeqMcpServer.Services;

public sealed class SeqConnectionFactory
{
    private readonly ICredentialStore _store;
    private readonly string _baseUrl;
    private readonly ILogger<SeqConnectionFactory>? _logger;

    public SeqConnectionFactory(IConfiguration cfg, ICredentialStore store, ILogger<SeqConnectionFactory>? logger = null)
    {
        // Try environment variable first, then fall back to configuration
        _baseUrl = Environment.GetEnvironmentVariable("SEQ_SERVER_URL") 
            ?? cfg["Seq:ServerUrl"] 
            ?? "http://localhost:5341";
        _store = store;
        _logger = logger;
        
        _logger?.LogInformation("SeqConnectionFactory initialized with URL: {Url}", _baseUrl);
    }

    public SeqConnection Create(string? workspace = null) 
    {
        var apiKey = _store.GetApiKey(workspace ?? "default");
        _logger?.LogInformation("Creating Seq connection to {Url} with API key: {ApiKey}", 
            _baseUrl, apiKey?[..Math.Min(5, apiKey?.Length ?? 0)] + "...");
        return new SeqConnection(_baseUrl, apiKey);
    }
}
