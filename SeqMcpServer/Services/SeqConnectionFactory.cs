using Microsoft.Extensions.Configuration;
using Seq.Api;

namespace SeqMcpServer.Services;

public sealed class SeqConnectionFactory
{
    private readonly ICredentialStore _store;
    private readonly string _baseUrl;

    public SeqConnectionFactory(IConfiguration cfg, ICredentialStore store)
    {
        // Try environment variable first, then fall back to configuration
        _baseUrl = Environment.GetEnvironmentVariable("SEQ_SERVER_URL") 
            ?? cfg["Seq:ServerUrl"] 
            ?? "http://localhost:5341";
        _store = store;
    }

    public SeqConnection Create(string? workspace = null) =>
        new(_baseUrl, _store.GetApiKey(workspace ?? "default"));
}