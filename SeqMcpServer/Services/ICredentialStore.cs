namespace SeqMcpServer.Services;

public interface ICredentialStore
{
    string GetApiKey(string workspace);
    void Reload();
}