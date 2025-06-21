using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeqMcpServer.Services;

public interface ICredentialStore
{
    string GetApiKey(string workspace);
    void Reload();
}

public sealed class FileCredentialStore : ICredentialStore
{
    private readonly string _path;
    private readonly bool _enableWatcher;
    private volatile Dictionary<string,string> _map = new();

    public FileCredentialStore(IConfiguration cfg, bool enableWatcher = true)
    {
        _path = cfg["CredentialFile"] ?? "secrets.json";
        _enableWatcher = enableWatcher;
        Reload();
        
        if (_enableWatcher && Directory.Exists(Path.GetDirectoryName(_path)))
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(_path)!)
            {
                Filter = Path.GetFileName(_path),
                EnableRaisingEvents = true
            };
            watcher.Changed += (_,_) => Reload();
        }
    }

    public string GetApiKey(string workspace) =>
        _map.TryGetValue(workspace ?? "default", out var key)
            ? key
            : throw new InvalidOperationException($"No API key for workspace {workspace}");

    public void Reload() =>
        _map = JsonSerializer.Deserialize<Dictionary<string,string>>(
                 File.ReadAllText(_path),
                 new JsonSerializerOptions {
                     PropertyNameCaseInsensitive = true,
                     UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
                 })!;
}