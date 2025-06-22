using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SeqMcpServer.Tests;

[Collection("McpIntegration")]
public class ApiKeyErrorTests : IAsyncLifetime
{
    private IMcpClient? _mcpClient;
    
    public async Task InitializeAsync()
    {
        // Create MCP client with invalid API key
        var testAssemblyLocation = Path.GetDirectoryName(typeof(ApiKeyErrorTests).Assembly.Location)!;
        var serverDllPath = Path.GetFullPath(Path.Combine(testAssemblyLocation, "../../../../../src/SeqMcpServer/bin/Debug/net9.0/SeqMcpServer.dll"));
        
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Seq MCP Server Test - Invalid Key",
            Command = "dotnet",
            Arguments = [serverDllPath],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["SEQ_SERVER_URL"] = "http://localhost:5341",
                ["SEQ_API_KEY"] = "InvalidApiKey123"
            }
        });
        
        _mcpClient = await McpClientFactory.CreateAsync(clientTransport);
    }
    
    public async Task DisposeAsync()
    {
        if (_mcpClient != null)
            await _mcpClient.DisposeAsync();
    }
    
    [Fact]
    public async Task SeqSearch_WithInvalidApiKey_ShouldReturnClearErrorMessage()
    {
        // Act
        var result = await _mcpClient!.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "@Level = 'Error'",
                ["count"] = 10
            });
        
        // Assert
        Assert.NotNull(result);
        
        var textContent = string.Join(" ", result.Content
            .OfType<TextContentBlock>()
            .Select(c => c.Text));
        
        // Log what we got
        Console.WriteLine($"Content length: {textContent.Length}");
        Console.WriteLine($"First 500 chars: {textContent.Substring(0, Math.Min(500, textContent.Length))}");
        
        // Should contain clear authentication error indication
        var hasAuthError = textContent.Contains("401") ||
                          textContent.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                          textContent.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                          textContent.Contains("error", StringComparison.OrdinalIgnoreCase);
        
        // Should NOT contain massive JSON response
        Assert.True(textContent.Length < 1000, $"Response too large ({textContent.Length} chars). Expected concise error message.");
        Assert.True(hasAuthError, "Expected clear authentication error message");
    }
}