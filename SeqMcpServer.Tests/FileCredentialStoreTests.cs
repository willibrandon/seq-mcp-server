using Microsoft.Extensions.Configuration;
using SeqMcpServer.Services;

namespace SeqMcpServer.Tests;

public class FileCredentialStoreTests : IDisposable
{
    private readonly string _tempFile;
    private readonly ICredentialStore _store;

    public FileCredentialStoreTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllText(_tempFile, """{ "default":"OLD" }""");
        
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        cfg["CredentialFile"] = _tempFile;
        
        _store = new FileCredentialStore(cfg, enableWatcher: false);
    }

    [Fact]
    public void GetApiKey_ReturnsCorrectKey()
    {
        Assert.Equal("OLD", _store.GetApiKey("default"));
    }

    [Fact]
    public void GetApiKey_ThrowsForMissingWorkspace()
    {
        Assert.Throws<InvalidOperationException>(() => _store.GetApiKey("nonexistent"));
    }

    [Fact]
    public void Reload_UpdatesKeys()
    {
        // Manually call reload instead of relying on FileSystemWatcher
        File.WriteAllText(_tempFile, """{ "default":"NEW" }""");
        _store.Reload();
        Assert.Equal("NEW", _store.GetApiKey("default"));
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}