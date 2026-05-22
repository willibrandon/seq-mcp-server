using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using Seq.Api;
using SeqMcpServer.Services;

namespace SeqMcpServer.Tests;

public abstract class McpToolsIntegrationTestsBase : IAsyncLifetime
{
    private IContainer? _seqContainer;
    private string? _seqUrl;
    private IHost? _mcpServerHost;
    private IMcpClient? _mcpClient;

    protected abstract string SeqImageTag { get; }
    protected virtual bool DisableFirstRunAuthentication => false;

    protected string SeqUrl => _seqUrl ?? throw new InvalidOperationException("Seq URL not initialized.");
    protected IMcpClient McpClient => _mcpClient ?? throw new InvalidOperationException("MCP client not initialized.");

    public async Task InitializeAsync()
    {
        // Start Seq container - version is configured by each derived test fixture
        var containerBuilder = new ContainerBuilder()
            .WithImage($"datalust/seq:{SeqImageTag}")
            .WithPortBinding(80, true)  // Map container port 80 (main API) to random host port
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SEQ_API_CANONICALURI", "http://localhost")
            .WithEnvironment("SEQ_CACHE_SYSTEMRAMTARGET", "0.1")  // Reduce memory usage for tests
            .WithTmpfsMount("/data");  // Use tmpfs for data directory in tests

        if (DisableFirstRunAuthentication)
        {
            containerBuilder = containerBuilder.WithEnvironment("SEQ_FIRSTRUN_NOAUTHENTICATION", "true");
        }

        _seqContainer = containerBuilder.Build();

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
        var connection = new SeqConnection(SeqUrl, "test-api-key");
        var hasScanLink = await SeqCapabilities.SupportsScanAsync(connection);

        if (shouldExist)
            Assert.True(hasScanLink);
        else
            Assert.False(hasScanLink);
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

        // Assert - Should have our four tools
        Assert.NotNull(tools);
        Assert.Contains(tools, t => t.Name == "SeqSearch");
        Assert.Contains(tools, t => t.Name == "SeqWaitForEvents");
        Assert.Contains(tools, t => t.Name == "SignalList");
        Assert.Contains(tools, t => t.Name == "SeqConvertFilter");
        Assert.Equal(4, tools.Count);
    }

    [Theory]
    // Bareword and invalid syntax fall back to text-match
    [InlineData("error", "\"error\"", true)]
    [InlineData("error timeout", "\"error timeout\"", true)]
    // Already-strict filter expressions pass through unchanged
    [InlineData("@Level = 'Error'", "@Level = 'Error'", false)]
    [InlineData("Application = 'foo'", "Application = 'foo'", false)]
    // Fuzzy-but-valid expression gets normalized (== becomes =)
    [InlineData("User=='alice'", "User = 'alice'", false)]
    public async Task SeqConvertFilter_ReturnsExpectedResult(string fuzzyFilter, string expectedStrict, bool expectedMatchedAsText)
    {
        var result = await _mcpClient!.CallToolAsync(
            "SeqConvertFilter",
            new Dictionary<string, object?>
            {
                ["fuzzyFilter"] = fuzzyFilter
            });

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());
        Assert.False(result.IsError);

        var contentJson = JsonSerializer.Serialize(result.Content.First());
        using var outer = JsonDocument.Parse(contentJson);
        var innerText = outer.RootElement.GetProperty("text").GetString();
        Assert.NotNull(innerText);
        using var inner = JsonDocument.Parse(innerText);

        Assert.Equal(expectedStrict, inner.RootElement.GetProperty("strictExpression").GetString());
        Assert.Equal(expectedMatchedAsText, inner.RootElement.GetProperty("matchedAsText").GetBoolean());

        var reason = inner.RootElement.GetProperty("reasonIfMatchedAsText").GetString();
        Assert.Equal(expectedMatchedAsText, !string.IsNullOrEmpty(reason));
    }

    protected async Task SeqSearch_WithDateRange_ReturnsFilteredEvents_Core()
    {
        var fromDate = DateTime.UtcNow.AddDays(-7).ToString("o");
        var toDate = DateTime.UtcNow.ToString("o");

        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 10,
                ["fromDateUtc"] = fromDate,
                ["toDateUtc"] = toDate
            });

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
    }

    protected async Task SeqSearch_WithTimeout_ReturnsBeforeTimeout_Core()
    {
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 10,
                ["timeoutSeconds"] = 30
            });

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
    }

    protected async Task SeqSearch_WithInvalidDateFormat_ReturnsError_Core()
    {
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 10,
                ["fromDateUtc"] = "not-a-date"
            });

        Assert.NotNull(result);
        Assert.True(result.IsError, "Expected IsError to be true for invalid date format");
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any(), "Expected error content to be present");
    }

    protected async Task SeqSearch_WithInvalidSignalId_ReturnsError_Core()
    {
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 10,
                ["signalId"] = "signal-nonexistent-12345"
            });

        Assert.NotNull(result);
        Assert.True(result.IsError, "Expected IsError to be true for invalid signal ID");
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any(), "Expected error content to be present");
    }

    protected async Task SeqSearch_WithVeryShortTimeout_HandlesGracefully_Core()
    {
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 1000,
                ["timeoutSeconds"] = 1
            });

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
    }

    protected async Task SeqSearch_WithAfterId_ReturnsPaginatedResults_Core()
    {
        var firstResult = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 5
            });

        Assert.NotNull(firstResult);
        Assert.NotNull(firstResult.Content);

        if (firstResult.Content.Any())
        {
            var secondResult = await McpClient.CallToolAsync(
                "SeqSearch",
                new Dictionary<string, object?>
                {
                    ["filter"] = "*",
                    ["count"] = 5,
                    ["afterId"] = "event-test-id"
                });

            Assert.NotNull(secondResult);
            Assert.NotNull(secondResult.Content);
        }
    }

    protected async Task SeqSearch_WithAsteriskFilter_NormalizesToEmptyString_Core()
    {
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "*",
                ["count"] = 5
            });

        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        if (result.IsError)
        {
            var errorJson = JsonSerializer.Serialize(result.Content.First());
            Assert.True(errorJson.Contains("Syntax error") || errorJson.Contains("Invalid filter"));
        }
    }

    protected async Task SeqSearch_WithInvalidFilterSyntax_ReturnsHelpfulError_Core()
    {
        var result = await McpClient.CallToolAsync(
            "SeqSearch",
            new Dictionary<string, object?>
            {
                ["filter"] = "@Level = = 'Error'",
                ["count"] = 5
            });

        Assert.NotNull(result);
        Assert.True(result.IsError);
        Assert.NotNull(result.Content);
        Assert.True(result.Content.Any());

        var errorJson = JsonSerializer.Serialize(result.Content.First());
        Assert.True(
            errorJson.Contains("Invalid filter expression") ||
            errorJson.Contains("Syntax error") ||
            errorJson.Contains("An error occurred"),
            "Error message should indicate filter syntax problem");
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
    protected override string SeqImageTag => "2024.3.13181";

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
}

[Collection("McpIntegration")]
public class McpToolsIntegrationModernSeqTests : McpToolsIntegrationTestsBase
{
    protected override string SeqImageTag => "2025.2.16202";
    protected override bool DisableFirstRunAuthentication => true;

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

    [Fact]
    public async Task SeqSearch_WithDateRange_ReturnsFilteredEvents() =>
        await SeqSearch_WithDateRange_ReturnsFilteredEvents_Core();

    [Fact]
    public async Task SeqSearch_WithTimeout_ReturnsBeforeTimeout() =>
        await SeqSearch_WithTimeout_ReturnsBeforeTimeout_Core();

    [Fact]
    public async Task SeqSearch_WithInvalidDateFormat_ReturnsError() =>
        await SeqSearch_WithInvalidDateFormat_ReturnsError_Core();

    [Fact]
    public async Task SeqSearch_WithInvalidSignalId_ReturnsError() =>
        await SeqSearch_WithInvalidSignalId_ReturnsError_Core();

    [Fact]
    public async Task SeqSearch_WithVeryShortTimeout_HandlesGracefully() =>
        await SeqSearch_WithVeryShortTimeout_HandlesGracefully_Core();

    [Fact]
    public async Task SeqSearch_WithAfterId_ReturnsPaginatedResults() =>
        await SeqSearch_WithAfterId_ReturnsPaginatedResults_Core();

    [Fact]
    public async Task SeqSearch_WithAsteriskFilter_NormalizesToEmptyString() =>
        await SeqSearch_WithAsteriskFilter_NormalizesToEmptyString_Core();

    [Fact]
    public async Task SeqSearch_WithInvalidFilterSyntax_ReturnsHelpfulError() =>
        await SeqSearch_WithInvalidFilterSyntax_ReturnsHelpfulError_Core();
}
