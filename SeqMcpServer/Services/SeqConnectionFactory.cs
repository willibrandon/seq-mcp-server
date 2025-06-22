using Microsoft.Extensions.Configuration;
using Seq.Api;

namespace SeqMcpServer.Services;

public sealed class SeqConnectionFactory
{
    private readonly ICredentialStore _store;
    private readonly string _baseUrl;

    public SeqConnectionFactory(IConfiguration cfg, ICredentialStore store) =>
        (_baseUrl, _store) = (cfg["Seq:ServerUrl"]!, store);

    public SeqConnection Create(string? workspace = null) =>
        new(_baseUrl, _store.GetApiKey(workspace ?? "default"));
}