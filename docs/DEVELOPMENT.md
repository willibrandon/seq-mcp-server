# Development Setup Guide

This guide explains how to set up your development environment for the Seq MCP Server.

## Quick Start

### Automated Setup (Recommended)

Use the provided setup scripts to automatically configure everything:

**PowerShell (Windows):**
```powershell
.\scripts\setup-dev.ps1
```

**Bash (Linux/Mac):**
```bash
./scripts/setup-dev.sh
```

The setup script will:
1. Start a Seq container on non-standard ports (15341 for API, 18081 for UI)
2. Configure Seq with development credentials
3. Automatically handle the initial password change requirement
4. Create a development API key
5. Set environment variables for your session
6. Create a `.env` file that the application automatically loads
7. Display the Seq UI URL and connection details

### Running the MCP Server

After setup, you can run the MCP server from anywhere:

```bash
# From command line
dotnet run --project SeqMcpServer

# Or from Visual Studio - just hit F5
```

The application automatically loads the `.env` file created by the setup script.

### Teardown

To stop and remove the development environment:

**PowerShell (Windows):**
```powershell
.\scripts\teardown-dev.ps1
```

**Bash (Linux/Mac):**
```bash
./scripts/teardown-dev.sh
```

## Manual Setup

If you prefer to set up manually:

1. Start Seq container:
```bash
docker run -d \
    --name seq-mcp-dev \
    -p 15341:5341 \
    -p 18081:80 \
    -e ACCEPT_EULA=Y \
    datalust/seq:latest
```

2. Create `.env` file in the project root:
```
SEQ_SERVER_URL=http://localhost:18081
SEQ_API_KEY=your-api-key-here
```

3. Create an API key via Seq UI at http://localhost:18081

## Environment Configuration

The application uses environment variables for configuration. These can be set via:

1. **`.env` file** (recommended for development) - Automatically loaded by the application
2. **System environment variables** - Set by the setup script for persistence
3. **Manual export/set** - For temporary use

### Required Variables

- `SEQ_SERVER_URL`: URL of your Seq server (e.g., `http://localhost:18081`)
- `SEQ_API_KEY`: API key for accessing Seq

### Workspace-Specific Keys (Optional)

You can configure different API keys for different workspaces:

```
SEQ_API_KEY=default-key
SEQ_API_KEY_PRODUCTION=production-key
SEQ_API_KEY_STAGING=staging-key
```

## Troubleshooting

### "SEQ_API_KEY environment variable is not set" Error

This means the application can't find the API key. The setup script should handle this automatically, but if you see this error:

1. Ensure you ran the setup script: `.\scripts\setup-dev.ps1`
2. Check that `.env` file exists in the project root
3. If running from Visual Studio, restart it to pick up new environment variables
4. Verify the `.env` file contains valid `SEQ_API_KEY` and `SEQ_SERVER_URL` values

### Port Conflicts

The setup scripts use non-standard ports to avoid conflicts:
- API: 15341 (instead of 5341)
- UI: 18081 (instead of 8081)

If these ports are in use, modify the scripts to use different ports.

### Container Already Exists

If you see "container name already in use", run the teardown script first:
```powershell
.\scripts\teardown-dev.ps1
```

### Password Change Required

The setup script automatically handles the initial password change. If doing manual setup, you'll need to:
1. Login with initial credentials
2. Change the password when prompted
3. Use the new password to create API keys

## Security Notes

⚠️ **Development Only**: The automated setup uses hardcoded development credentials. Never use these in production!

For production:
- Use strong, unique passwords
- Store credentials securely (e.g., Azure Key Vault, environment variables)
- Enable proper authentication and authorization
- Use HTTPS for all connections