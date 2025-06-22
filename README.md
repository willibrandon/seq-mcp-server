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

### Production

```bash
# Set environment variables
export SEQ_SERVER_URL="http://your-seq-server:5341"
export SEQ_API_KEY="your-api-key"

# Run the MCP server
dotnet run --project SeqMcpServer --configuration Release
```

## MCP Tools

The following tools are available through the MCP protocol:

- **`SeqSearch`** - Search Seq events with filters
  - Parameters: `filter`, `count`, `workspace` (optional)
  - Returns: List of matching events

- **`SeqStream`** - Stream live events from Seq (5-second timeout)
  - Parameters: `filter` (optional), `count`, `workspace` (optional)
  - Returns: List of recent events

- **`SignalList`** - List available signals
  - Parameters: `shared`, `own`, `workspace` (optional)
  - Returns: List of signals

## Configuration

The Seq MCP Server uses environment variables for configuration:

- `SEQ_SERVER_URL`: URL of your Seq server (defaults to `http://localhost:5341`)
- `SEQ_API_KEY`: API key for accessing Seq (required)
- `SEQ_API_KEY_<WORKSPACE>`: Optional workspace-specific API keys (e.g., `SEQ_API_KEY_PRODUCTION`)

### Workspace Support

You can configure different API keys for different workspaces:

```bash
export SEQ_API_KEY="default-key"
export SEQ_API_KEY_PRODUCTION="production-key"
export SEQ_API_KEY_STAGING="staging-key"
```

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

## License

[Your License Here]