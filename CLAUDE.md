# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Development Environment Setup
```bash
# Setup development environment (starts Seq container, creates API key)
# PowerShell
./scripts/setup-dev.ps1

# Bash
./scripts/setup-dev.sh

# Teardown development environment
# PowerShell
./scripts/teardown-dev.ps1

# Bash
./scripts/teardown-dev.sh
```

### Build and Run
```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Set environment variables (required)
export SEQ_SERVER_URL="http://localhost:5341"
export SEQ_API_KEY="your-api-key"

# Run the application (MCP server mode)
dotnet run --project src/SeqMcpServer

# Run with Docker Compose (includes Seq server)
docker compose up
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run a specific test class
dotnet test --filter "FullyQualifiedName~FileCredentialStoreTests"

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Docker
```bash
# Build Docker image
docker build -t seq-mcp-server .

# Run with Docker Compose
docker compose up -d

# View logs
docker compose logs -f
```

## Architecture

### Application Mode
The application runs as a Model Context Protocol (MCP) server using stdio transport, exposing Seq tools to MCP clients.

### Core Components

**MCP Tools** (src/SeqMcpServer/Mcp/SeqTools.cs):
- `SeqSearch`: Search Seq events with filters (count, filter, columns, render)
- `SeqStream`: Stream live events from Seq (5-second timeout)
- `SignalList`: List available signals (read-only)

**Services**:
- `EnvironmentCredentialStore` (src/SeqMcpServer/Services/EnvironmentCredentialStore.cs): Manages API keys from environment variables
- `SeqConnectionFactory` (src/SeqMcpServer/Services/SeqConnectionFactory.cs): Creates Seq API connections per workspace

**Configuration**:
- API keys provided via environment variables:
  - `SEQ_API_KEY`: Default API key
  - `SEQ_API_KEY_<WORKSPACE>`: Workspace-specific API keys (optional)
- Seq server URL via `SEQ_SERVER_URL` environment variable or `appsettings.json`
- Version constraints: Min 2024.1, Max 2025.1

### Testing Strategy
- Unit tests for core services (e.g., EnvironmentCredentialStoreTests)
- Integration tests using Testcontainers to spin up real Seq instances
- Tests located in `tests/SeqMcpServer.Tests` project

### Project Principles
- KISS (Keep It Simple, Stupid) - avoid over-engineering
- YAGNI (You Aren't Gonna Need It) - implement only what's needed
- Target: Core code under 1,000 LOC
- Vendor-neutral approach (no external provider SDKs)

## Key Files to Understand
- `src/SeqMcpServer/Program.cs`: Entry point for MCP server
- `src/SeqMcpServer/Mcp/SeqTools.cs`: MCP tool implementations
- `src/SeqMcpServer/Services/EnvironmentCredentialStore.cs`: Credential management
- `docs/PRD.md`: Product requirements and design decisions
- `docker-compose.yml`: Local development setup with Seq