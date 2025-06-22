# Seq MCP Server

A Model Context Protocol (MCP) server that provides tools for searching and streaming events from Seq.

## Quick Start

### Development Environment

```bash
# Clone the repository
git clone https://github.com/your-org/seq-mcp-server
cd seq-mcp-server

# Setup development environment (fully automated)
# PowerShell (Windows)
./scripts/setup-dev.ps1

# Bash (Linux/Mac)
./scripts/setup-dev.sh

# Build and run the MCP server
dotnet build
dotnet run --project SeqMcpServer
```

The setup script automatically:
- Starts a Seq container on ports 15341/18081
- Configures authentication and creates an API key
- Sets up environment variables
- Creates a `.env` file for the application

### Production Deployment

MCP servers are not run directly - they are launched by MCP clients. For production:

1. Build and deploy the executable:
```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
```

2. Configure your MCP client to use the deployed executable:
```json
{
  "mcpServers": {
    "seq": {
      "command": "/path/to/seq-mcp-server",
      "env": {
        "SEQ_SERVER_URL": "http://your-seq-server:5341",
        "SEQ_API_KEY": "your-production-api-key"
      }
    }
  }
}
```

## MCP Tools

The following tools are available through the MCP protocol:

- **`SeqSearch`** - Search Seq events with filters
  - Parameters: 
    - `filter` (required): Seq filter expression (use empty string `""` for all events)
    - `count`: Number of events to return (default: 100)
    - `workspace` (optional): Specific workspace to query
  - Returns: List of matching events
  - Example filters:
    - `""` - all events
    - `"error"` - events containing "error"
    - `@Level = "Error"` - error level events
    - `Application = "MyApp"` - events from specific application

- **`SeqStream`** - Stream live events from Seq (5-second timeout)
  - Parameters: 
    - `filter` (optional): Seq filter expression
    - `count`: Number of events to return (default: 10)
    - `workspace` (optional): Specific workspace to query
  - Returns: List of recent events

- **`SignalList`** - List available signals (read-only)
  - Parameters: 
    - `workspace` (optional): Specific workspace to query
  - Returns: List of signals with their definitions

## Claude Desktop Integration

### Option 1: Pre-built Release (Recommended)

Download the latest release for your platform and add to your MCP settings:

```json
{
  "mcpServers": {
    "seq": {
      "command": "C:\\\\Tools\\\\seq-mcp-server.exe",
      "args": [],
      "env": {
        "SEQ_SERVER_URL": "http://localhost:5341",
        "SEQ_API_KEY": "your-api-key-here"
      }
    }
  }
}
```

### Option 2: Build from Source

Build a single-file executable (requires .NET 9 runtime):

```bash
# Windows
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true

# macOS
dotnet publish -c Release -r osx-x64 -p:PublishSingleFile=true

# Linux
dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true
```

The executable will be in `SeqMcpServer/bin/Release/net9.0/{runtime}/publish/`

## Configuration

The Seq MCP Server uses environment variables for configuration:

- `SEQ_SERVER_URL`: URL of your Seq server
- `SEQ_API_KEY`: API key for accessing Seq (required)
- `SEQ_API_KEY_<WORKSPACE>`: Optional workspace-specific API keys (e.g., `SEQ_API_KEY_PRODUCTION`)

### Workspace Support

The MCP server supports workspace-specific API keys (future feature):

```bash
export SEQ_API_KEY="default-key"
export SEQ_API_KEY_PRODUCTION="production-key"
export SEQ_API_KEY_STAGING="staging-key"
```

*Note: Workspace-specific keys are currently designed but not yet implemented in the MCP tools.*

## Development

### Prerequisites

- .NET 9.0 SDK
- Docker (for running Seq locally)

### Running Tests

```bash
dotnet test
```

### Development

The `scripts` folder contains automated setup scripts:

- **`setup-dev.ps1` / `setup-dev.sh`**: Automatically configures your development environment
  - Starts Seq container with authentication
  - Handles initial password setup
  - Creates development API key
  - Sets environment variables
  - Creates `.env` file for the application
  
- **`teardown-dev.ps1` / `teardown-dev.sh`**: Cleans up the development environment
  - Stops and removes containers
  - Clears environment variables

For detailed development setup, see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Architecture

This is a pure MCP server implementation that:
- Runs as a stdio-based service (no web server)
- Communicates via JSON-RPC over standard input/output
- Does not log to console to avoid interfering with MCP communication
- Optionally logs to Seq itself for debugging when configured

### Self-Logging

The MCP server can log its own operations to Seq when a valid `SEQ_SERVER_URL` and `SEQ_API_KEY` are provided. This helps with debugging and monitoring the MCP server itself.

## License

MIT License - see [LICENSE](LICENSE) file for details.