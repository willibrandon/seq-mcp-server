using Microsoft.Extensions.Configuration;
using SeqMcpServer.Services;

namespace SeqMcpServer.Tests;

public class FileCredentialStoreTests
{
    [Fact]
    public void CredentialReload()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, """{ "default":"OLD" }""");
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        cfg["CredentialFile"] = tmp;

        var store = new FileCredentialStore(cfg);
        Assert.Equal("OLD", store.GetApiKey("default"));

        File.WriteAllText(tmp, """{ "default":"NEW" }""");
        Thread.Sleep(200);             // give watcher time
        Assert.Equal("NEW", store.GetApiKey("default"));
        
        File.Delete(tmp);
    }
}