using SeqMcpServer.Services;

namespace SeqMcpServer.Tests;

public class EnvironmentCredentialStoreTests
{
    [Fact]
    public void GetApiKey_ReturnsDefaultKey()
    {
        // Arrange
        Environment.SetEnvironmentVariable("SEQ_API_KEY", "test-key");
        
        try
        {
            // Act
            var store = new EnvironmentCredentialStore();
            var key = store.GetApiKey("default");
            
            // Assert
            Assert.Equal("test-key", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEQ_API_KEY", null);
        }
    }
    
    [Fact]
    public void GetApiKey_ReturnsWorkspaceSpecificKey()
    {
        // Arrange
        Environment.SetEnvironmentVariable("SEQ_API_KEY", "default-key");
        Environment.SetEnvironmentVariable("SEQ_API_KEY_PRODUCTION", "prod-key");
        
        try
        {
            // Act
            var store = new EnvironmentCredentialStore();
            var defaultKey = store.GetApiKey("default");
            var prodKey = store.GetApiKey("production");
            
            // Assert
            Assert.Equal("default-key", defaultKey);
            Assert.Equal("prod-key", prodKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEQ_API_KEY", null);
            Environment.SetEnvironmentVariable("SEQ_API_KEY_PRODUCTION", null);
        }
    }

    [Fact]
    public void Constructor_ThrowsWhenNoApiKeySet()
    {
        // Arrange
        var originalKey = Environment.GetEnvironmentVariable("SEQ_API_KEY");
        Environment.SetEnvironmentVariable("SEQ_API_KEY", null);
        
        try
        {
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => new EnvironmentCredentialStore());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEQ_API_KEY", originalKey);
        }
    }
    
    [Fact]
    public void GetApiKey_FallsBackToDefaultForUnknownWorkspace()
    {
        // Arrange
        Environment.SetEnvironmentVariable("SEQ_API_KEY", "default-key");
        
        try
        {
            // Act
            var store = new EnvironmentCredentialStore();
            var key = store.GetApiKey("unknown-workspace");
            
            // Assert
            Assert.Equal("default-key", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEQ_API_KEY", null);
        }
    }
}