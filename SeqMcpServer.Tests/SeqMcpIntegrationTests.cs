using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeqMcpServer.Services;

namespace SeqMcpServer.Tests;

[Collection("SeqIntegration")]
public class SeqMcpIntegrationTests : IClassFixture<SeqMcpIntegrationTests.SeqMcpWebApplicationFactory>, IAsyncLifetime
{
    private readonly SeqMcpWebApplicationFactory _factory;
    private IContainer? _seqContainer;
    private string? _seqUrl;

    public SeqMcpIntegrationTests(SeqMcpWebApplicationFactory factory)
    {
        _factory = factory;
    }

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
        Console.WriteLine($"Seq container URL: {_seqUrl} (hostname: {hostname}, API port: {port})");

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
                Console.WriteLine($"Testing Seq API readiness at: {_seqUrl}/api (attempt {++retryCount})");
                var response = await httpClient.GetAsync($"{_seqUrl}/api");
                if (response.IsSuccessStatusCode)
                {
                    seqReady = true;
                    Console.WriteLine($"Seq API is ready after {retryCount} attempts!");
                    break;
                }
                Console.WriteLine($"Seq API not ready, status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Seq API connection failed: {ex.Message}");
            }
            
            // Progressive delay: start with 1s, then 2s, then 3s for subsequent attempts
            var delay = Math.Min(1000 + (retryCount * 500), 3000);
            await Task.Delay(delay);
        }
        
        if (!seqReady)
        {
            throw new InvalidOperationException($"Seq API did not become ready at {_seqUrl}/api");
        }

        // Configure the factory with the Seq URL
        _factory.SeqUrl = _seqUrl;
        Console.WriteLine($"Setting WebApplicationFactory SeqUrl to: {_seqUrl}");
    }

    public async Task DisposeAsync()
    {
        if (_seqContainer != null)
            await _seqContainer.DisposeAsync();
    }

    [Fact]
    public async Task HealthEndpoint_WithRunningSeq_ReturnsOk()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Health endpoint response status: {response.StatusCode}, content: {content}");
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Health endpoint failed with status {response.StatusCode}: {content}");
        }
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("ok", content);
    }

    [Fact]
    public async Task SeqConnectionFactory_CanConnectToSeq()
    {
        // Arrange
        Console.WriteLine($"SeqConnectionFactory test - Seq URL should be: {_seqUrl}");
        
        // Create a direct connection using the same URL we verified works
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """{ "default": "" }""");
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Seq:ServerUrl", _seqUrl),
                new KeyValuePair<string, string?>("CredentialFile", tempFile)
            })
            .Build();
            
        var store = new FileCredentialStore(config, enableWatcher: false);
        var factory = new SeqConnectionFactory(config, store);
        
        Console.WriteLine($"Created factory with URL: {_seqUrl}");

        // Act & Assert - Should not throw
        var connection = factory.Create();
        Console.WriteLine($"Created connection with base URL: {connection.Client.ServerUrl}");
        
        try
        {
            var signals = await connection.Signals.ListAsync(shared: true);
            Console.WriteLine($"Successfully retrieved {signals.Count} signals");
            
            // Signals list should be accessible (even if empty)
            Assert.NotNull(signals);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
            Console.WriteLine($"Connection URL was: {connection.Client.ServerUrl}");
            throw;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PrometheusMetrics_ReturnsMetrics()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("# HELP", content); // Prometheus metrics format
    }

    public class SeqMcpWebApplicationFactory : WebApplicationFactory<Program>
    {
        public string? SeqUrl { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                var tempFile = Path.GetTempFileName();
                File.WriteAllText(tempFile, """{ "default": "" }""");  // Empty API key for anonymous access
                
                var configPairs = new List<KeyValuePair<string, string?>>
                {
                    new("CredentialFile", tempFile),
                    new("SeqVersion:Min", "2024.1"),
                    new("SeqVersion:Max", "2025.1")
                };
                
                if (!string.IsNullOrEmpty(SeqUrl))
                {
                    configPairs.Add(new("Seq:ServerUrl", SeqUrl));
                    System.Console.WriteLine($"WebAppFactory: Configuring Seq URL: {SeqUrl}");
                }
                else
                {
                    System.Console.WriteLine("WebAppFactory: SeqUrl is null or empty");
                }
                
                config.AddInMemoryCollection(configPairs);
            });

            builder.ConfigureServices((context, services) =>
            {
                // Remove existing services and re-add with proper configuration
                var descriptors = services.Where(d => 
                    d.ServiceType == typeof(SeqConnectionFactory) || 
                    d.ServiceType == typeof(ICredentialStore)).ToArray();
                
                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                // Re-add services with updated configuration
                services.AddSingleton<ICredentialStore, FileCredentialStore>();
                services.AddSingleton<SeqConnectionFactory>();
            });
            
            builder.UseEnvironment("Testing");
        }
    }

    public class TestConfiguration
    {
        public string TempSecretsFile { get; set; } = string.Empty;
    }
}