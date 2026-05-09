using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using SeqMcpServer.Services;

namespace SeqMcpServer.Tests;

public abstract class McpToolsIntegrationTestsBase : IAsyncLifetime
{
    private IContainer? _seqContainer;
    private string? _seqUrl;
    private IHost? _mcpServerHost;
    private IMcpClient? _mcpClient;

    protected abstract string SeqImageTag { get; }

    protected string SeqUrl => _seqUrl ?? throw new InvalidOperationException("Seq URL not initialized.");
    protected IMcpClient McpClient => _mcpClient ?? throw new InvalidOperationException("MCP client not initialized.");

    public async Task InitializeAsync()
    {
        // Start Seq container - version is configured by each derived test fixture
        _seqContainer = new ContainerBuilder()
            .WithImage($"datalust/seq:{SeqImageTag}")
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

        // Set environment variables for the test
        Environment.SetEnvironmentVariable("SEQ_SERVER_URL", _seqUrl);
        Environment.SetEnvironmentVariable("SEQ_API_KEY", "test-api-key");

        // Start MCP server (for in-process testing if needed)
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ICredentialStore, EnvironmentCredentialStore>();
            
            services.AddSingleton<SeqConnectionFactory>(provider =>
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("Seq:ServerUrl", _seqUrl)
                    ])
                    .Build();
                var store = provider.GetRequiredService<ICredentialStore>();
                return new SeqConnectionFactory(config, store);
            });

            // Add MCP server with tools (not needed in test)
        });

        _mcpServerHost = builder.Build();
        await _mcpServerHost.StartAsync();

        // Create MCP client transport
        // Build path relative to the test assembly location
        var testAssemblyLocation = Path.GetDirectoryName(typeof(McpToolsIntegrationTestsBase).Assembly.Location)!;
        var serverDllPath = Path.GetFullPath(Path.Combine(testAssemblyLocation, "../../../../../src/SeqMcpServer/bin/Debug/net10.0/SeqMcpServer.dll"));
        
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Seq MCP Server Test",
            Command = "dotnet",
            Arguments = [serverDllPath, $"--Seq:ServerUrl={_seqUrl}", "--SeqVersion:Min=2024.1", "--SeqVersion:Max=2025.2"],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["SEQ_SERVER_URL"] = _seqUrl,
                ["SEQ_API_KEY"] = "test-api-key"
            }
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

    protected async Task SeqSearch_WithValidFilter_ReturnsEvents_Core()
    {
        await WriteTestEventAsync("Compatibility smoke event", "CompatibilitySmoke", true);

        // Arrange - Get available tools
        var tools = await McpClient.ListToolsAsync();
        var seqSearchTool = tools.FirstOrDefault(t => t.Name == "SeqSearch");
        Assert.NotNull(seqSearchTool);

        // Act - Call the seq_search tool via MCP
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "CompatibilitySmoke = true",
                ["count"] = 10
            });

        // Assert - Should return valid result
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
    }

    protected async Task SeqSearch_WithPropertyFilter_ReturnsEvents(string propertyName)
    {
        await WriteTestEventAsync("Compatibility fixture event", propertyName, true);

        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = $"{propertyName} = true",
                ["count"] = 10
            });

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
    }

    protected async Task AssertScanLinkAvailabilityAsync(bool shouldExist)
    {
        using var httpClient = new HttpClient();
        var eventsApi = await httpClient.GetStringAsync($"{SeqUrl}/api/events");

        if (shouldExist)
            Assert.Contains("Scan", eventsApi, StringComparison.OrdinalIgnoreCase);
        else
            Assert.DoesNotContain("Scan", eventsApi, StringComparison.OrdinalIgnoreCase);
    }

    protected async Task SignalList_ReturnsSignals_Core()
    {
        // Arrange - Get available tools
        var tools = await McpClient.ListToolsAsync();
        var signalListTool = tools.FirstOrDefault(t => t.Name == "SignalList");
        Assert.NotNull(signalListTool);

        // Act - Call the signal_list tool via MCP
        var result = await McpClient.CallToolAsync("SignalList", new Dictionary<string, object?>());

        // Assert - Should return valid result
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
    }

    protected async Task SeqWaitForEvents_CanCaptureEvents_Core()
    {
        // Arrange - Get available tools
        var tools = await McpClient.ListToolsAsync();
        var seqWaitTool = tools.FirstOrDefault(t => t.Name == "SeqWaitForEvents");
        Assert.NotNull(seqWaitTool);

        // Act - Call the SeqWaitForEvents tool via MCP
        var result = await McpClient.CallToolAsync(
            "SeqWaitForEvents",
            new Dictionary<string, object?>
            {
                ["filter"] = "*"
            });

        // Assert - Should return valid result (may be empty if no events in 5 seconds)
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        // Note: Content might be empty if no events occurred during the wait period
    }

    protected async Task MCP_Client_CanListTools_Core()
    {
        // Act - List available tools via MCP
        var tools = await McpClient.ListToolsAsync();

        // Assert - Should have our three tools
        Assert.NotNull(tools);
        Assert.Contains(tools, t => t.Name == "SeqSearch");
        Assert.Contains(tools, t => t.Name == "SeqWaitForEvents");
        Assert.Contains(tools, t => t.Name == "SignalList");
        Assert.Equal(3, tools.Count);
    }

    private async Task WriteTestEventAsync(string messageTemplate, string propertyName, bool propertyValue)
    {
        ArgumentNullException.ThrowIfNull(_seqUrl);

        using var httpClient = new HttpClient();
        var clef = $$"""
            {"@t":"{{DateTimeOffset.UtcNow:O}}","@mt":"{{messageTemplate}}","{{propertyName}}":{{propertyValue.ToString().ToLowerInvariant()}}}
            """;

        using var content = new StringContent(clef, System.Text.Encoding.UTF8, "application/vnd.serilog.clef");
        using var response = await httpClient.PostAsync($"{_seqUrl}/api/events/raw?clef", content);
        response.EnsureSuccessStatusCode();
    }
}

[Collection("McpIntegration")]
public class McpToolsIntegrationLegacySeqTests : McpToolsIntegrationTestsBase
{
    protected override string SeqImageTag => "2024.3";

    [Fact]
    public async Task SeqSearch_WithValidFilter_ReturnsEvents() =>
        await SeqSearch_WithValidFilter_ReturnsEvents_Core();

    [Fact]
    public async Task SignalList_ReturnsSignals() =>
        await SignalList_ReturnsSignals_Core();

    [Fact]
    public async Task SeqWaitForEvents_CanCaptureEvents() =>
        await SeqWaitForEvents_CanCaptureEvents_Core();

    [Fact]
    public async Task MCP_Client_CanListTools() =>
        await MCP_Client_CanListTools_Core();

    [Fact]
    public async Task SeqSearch_WithSeq2024_3WithoutScanLink_FallsBackToPagedEnumeration()
    {
        await AssertScanLinkAvailabilityAsync(shouldExist: false);
        await SeqSearch_WithPropertyFilter_ReturnsEvents("CompatibilityFallback");
    }
}

[Collection("McpIntegration")]
public class McpToolsIntegrationModernSeqTests : McpToolsIntegrationTestsBase
{
    protected override string SeqImageTag => "2025.2";

    [Fact]
    public async Task SeqSearch_WithValidFilter_ReturnsEvents() =>
        await SeqSearch_WithValidFilter_ReturnsEvents_Core();

    [Fact]
    public async Task SignalList_ReturnsSignals() =>
        await SignalList_ReturnsSignals_Core();

    [Fact]
    public async Task SeqWaitForEvents_CanCaptureEvents() =>
        await SeqWaitForEvents_CanCaptureEvents_Core();

    [Fact]
    public async Task MCP_Client_CanListTools() =>
        await MCP_Client_CanListTools_Core();

    [Fact]
    public async Task SeqSearch_WithSeq2025_2WithScanLink_CoversDirectScanPath()
    {
        await AssertScanLinkAvailabilityAsync(shouldExist: true);
        await SeqSearch_WithPropertyFilter_ReturnsEvents("CompatibilityScan");
    }
}
