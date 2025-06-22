using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using SeqMcpServer.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SeqMcpServer.Tests;

[Collection("McpIntegration")]
public class McpToolsIntegrationTests : IAsyncLifetime
{
    private IContainer? _seqContainer;
    private string? _seqUrl;
    private IHost? _mcpServerHost;
    private IMcpClient? _mcpClient;

    public async Task InitializeAsync()
    {
        // Start Seq container - use stable version and proper configuration
        _seqContainer = new ContainerBuilder()
            .WithImage("datalust/seq:2024.3")  // Use specific stable version
            .WithPortBinding(80, true)  // Map container port 80 (main API) to random host port
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SEQ_API_CANONICALURI", "http://localhost")
            .WithEnvironment("SEQ_CACHE_SYSTEMRAMTARGET", "0.1")  // Reduce memory usage for tests
            .WithTmpfsMount("/data")  // Use tmpfs for data directory in tests
            .Build();

        await _seqContainer.StartAsync();
        
        // Use container hostname and mapped port for port 80 (main API)
        var hostname = _seqContainer.Hostname;
        var port = _seqContainer.GetMappedPublicPort(80);
        _seqUrl = $"http://{hostname}:{port}";
        Console.WriteLine($"MCP Tests - Seq container URL: {_seqUrl} (hostname: {hostname}, API port: {port})");

        // Wait for Seq API to be ready on port 80 - Seq can take time to initialize  
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var retries = 30;  // 30 retries with progressive delays
        var seqReady = false;
        var retryCount = 0;
        
        while (retries-- > 0 && !seqReady)
        {
            try
            {
                Console.WriteLine($"MCP Tests - Testing Seq API readiness at: {_seqUrl}/api (attempt {++retryCount})");
                var response = await httpClient.GetAsync($"{_seqUrl}/api");
                if (response.IsSuccessStatusCode)
                {
                    seqReady = true;
                    Console.WriteLine($"MCP Tests - Seq API is ready after {retryCount} attempts!");
                    break;
                }
                Console.WriteLine($"MCP Tests - Seq API not ready, status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MCP Tests - Seq API connection failed: {ex.Message}");
            }
            
            // Progressive delay: start with 1s, then 2s, then 3s for subsequent attempts
            var delay = Math.Min(1000 + (retryCount * 500), 3000);
            await Task.Delay(delay);
        }
        
        if (!seqReady)
        {
            throw new InvalidOperationException($"MCP Tests - Seq API did not become ready at {_seqUrl}/api");
        }

        // Create test credentials file
        var tempSecretsFile = Path.GetTempFileName();
        File.WriteAllText(tempSecretsFile, """{ "default": "" }""");  // Empty API key for anonymous access

        // Start MCP server
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ICredentialStore>(provider =>
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("CredentialFile", tempSecretsFile)
                    })
                    .Build();
                return new FileCredentialStore(config, enableWatcher: false);
            });
            
            services.AddSingleton<SeqConnectionFactory>(provider =>
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("Seq:ServerUrl", _seqUrl)
                    })
                    .Build();
                var store = provider.GetRequiredService<ICredentialStore>();
                return new SeqConnectionFactory(config, store);
            });

            // Add MCP server with tools (not needed in test)
        });

        _mcpServerHost = builder.Build();
        await _mcpServerHost.StartAsync();

        // Create temporary secrets file for the MCP server
        var tempSecretsFileForServer = Path.GetTempFileName();
        File.WriteAllText(tempSecretsFileForServer, """{ "default": "test-api-key" }""");
        
        // Create MCP client transport
        var serverExecutable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "SeqMcpServer.exe" : "SeqMcpServer";
        var serverPath = Path.Combine("../../../SeqMcpServer/bin/Debug/net9.0", serverExecutable);
        
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Seq MCP Server Test",
            Command = "dotnet",
            Arguments = ["../../../../SeqMcpServer/bin/Debug/net9.0/SeqMcpServer.dll", $"--Seq:ServerUrl={_seqUrl}", $"--CredentialFile={tempSecretsFileForServer}", "--SeqVersion:Min=2024.1", "--SeqVersion:Max=2025.1"]
        });
        
        _mcpClient = await McpClientFactory.CreateAsync(clientTransport);
    }

    public async Task DisposeAsync()
    {
        if (_mcpClient != null)
            await _mcpClient.DisposeAsync();

        // Client disposal will handle the server process

        if (_mcpServerHost != null)
            await _mcpServerHost.StopAsync();

        if (_seqContainer != null)
            await _seqContainer.DisposeAsync();
    }

    [Fact]
    public async Task SeqSearch_WithValidFilter_ReturnsEvents()
    {
        // Arrange - Get available tools
        var tools = await _mcpClient!.ListToolsAsync();
        var seqSearchTool = tools.FirstOrDefault(t => t.Name == "SeqSearch");
        Assert.NotNull(seqSearchTool);

        // Act - Call the seq_search tool via MCP
        var result = await _mcpClient!.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 10
            });

        // Assert - Should return valid result
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
    }

    [Fact]
    public async Task SignalList_ReturnsSignals()
    {
        // Arrange - Get available tools
        var tools = await _mcpClient!.ListToolsAsync();
        var signalListTool = tools.FirstOrDefault(t => t.Name == "SignalList");
        Assert.NotNull(signalListTool);

        // Act - Call the signal_list tool via MCP
        var result = await _mcpClient!.CallToolAsync("SignalList", new Dictionary<string, object?>());

        // Assert - Should return valid result
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
    }

    [Fact]
    public async Task SeqStream_CanStartStreaming()
    {
        // Arrange - Get available tools
        var tools = await _mcpClient!.ListToolsAsync();
        var seqStreamTool = tools.FirstOrDefault(t => t.Name == "SeqStream");
        Assert.NotNull(seqStreamTool);

        // Act - Call the seq_stream tool via MCP
        var result = await _mcpClient!.CallToolAsync(
            "SeqStream",
            new Dictionary<string, object?>
            {
                ["filter"] = "*"
            });

        // Assert - Should return valid result
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
    }

    [Fact]
    public async Task MCP_Client_CanListTools()
    {
        // Act - List available tools via MCP
        var tools = await _mcpClient!.ListToolsAsync();

        // Assert - Should have our three tools
        Assert.NotNull(tools);
        Assert.Contains(tools, t => t.Name == "SeqSearch");
        Assert.Contains(tools, t => t.Name == "SeqStream");
        Assert.Contains(tools, t => t.Name == "SignalList");
        Assert.Equal(3, tools.Count);
    }
}