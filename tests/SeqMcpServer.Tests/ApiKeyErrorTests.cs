using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SeqMcpServer.Tests;

[Collection("McpIntegration")]
public class ApiKeyErrorTests : IAsyncLifetime
{
    private IContainer? _seqContainer;
    private string? _seqUrl;
    private IMcpClient? _mcpClient;
    
    public async Task InitializeAsync()
    {
        // Start Seq container with authentication required
        _seqContainer = new ContainerBuilder()
            .WithImage("datalust/seq:2024.3")
            .WithPortBinding(80, true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SEQ_API_CANONICALURI", "http://localhost")
            .WithEnvironment("SEQ_CACHE_SYSTEMRAMTARGET", "0.1")
            .WithEnvironment("SEQ_FIRSTRUN_ADMINUSERNAME", "admin")
            .WithEnvironment("SEQ_FIRSTRUN_ADMINPASSWORD", "admin123")
            .WithEnvironment("SEQ_FIRSTRUN_REQUIREAUTHENTICATIONFORHTTPINGESTION", "true")
            .WithEnvironment("SEQ_FIRSTRUN_DISABLEANONYMOUSREADACCESS", "true")
            .WithTmpfsMount("/data")
            .Build();

        await _seqContainer.StartAsync();
        
        var hostname = _seqContainer.Hostname;
        var port = _seqContainer.GetMappedPublicPort(80);
        _seqUrl = $"http://{hostname}:{port}";
        
        // Wait for Seq to be ready
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var retries = 30;
        var seqReady = false;
        
        while (retries-- > 0 && !seqReady)
        {
            try
            {
                var response = await httpClient.GetAsync($"{_seqUrl}/api");
                if (response.IsSuccessStatusCode)
                {
                    seqReady = true;
                    break;
                }
            }
            catch
            {
                // Ignore and retry
            }

            await Task.Delay(1000);
        }
        
        if (!seqReady)
        {
            throw new InvalidOperationException($"Seq did not become ready at {_seqUrl}");
        }
        
        // Create MCP client with invalid API key
        var testAssemblyLocation = Path.GetDirectoryName(typeof(ApiKeyErrorTests).Assembly.Location)!;
        var serverDllPath = Path.GetFullPath(Path.Combine(testAssemblyLocation, "../../../../../src/SeqMcpServer/bin/Debug/net9.0/SeqMcpServer.dll"));
        
        // Create a logger factory for the client transport
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Seq MCP Server Test - Invalid Key",
            Command = "dotnet",
            Arguments = [serverDllPath],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["SEQ_SERVER_URL"] = _seqUrl,
                ["SEQ_API_KEY"] = "InvalidApiKey123",
                ["SKIP_ENV_FILE"] = "true"
            }
        }, loggerFactory);
        
        _mcpClient = await McpClientFactory.CreateAsync(clientTransport);
    }
    
    public async Task DisposeAsync()
    {
        if (_mcpClient != null)
            await _mcpClient.DisposeAsync();
            
        if (_seqContainer != null)
            await _seqContainer.DisposeAsync();
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

        var textContent = string.Join(" ", result.Content
            .OfType<TextContentBlock>()
            .Select(c => c.Text));

        // Log what we got
        Console.WriteLine($"Content length: {textContent.Length}");
        Console.WriteLine($"First 500 chars: {textContent[..Math.Min(65656, textContent.Length)]}");

        // Should contain clear authentication error indication
        var hasAuthError = textContent.Contains("401") ||
                          textContent.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                          textContent.Contains("authentication", StringComparison.OrdinalIgnoreCase);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsError);

        Assert.True(textContent.Length < 1000);
        Assert.True(hasAuthError, "Expected clear authentication error message");
    }
}