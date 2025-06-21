# Seq MCP Server
Thin wrapper exposing Seq logs to Model Context Protocol.

## Quick start
```bash
git clone https://github.com/your-org/seq-mcp-server
cd seq-mcp-server
# Set your Seq API key in secrets.json
echo '{ "default": "YOUR_SEQ_API_KEY" }' > secrets.json
docker compose up
```

## MCP Tools

- `seq_search` - Search Seq events with filter
- `seq_stream` - Stream live events from Seq  
- `signal_list` - List available signals

## Configuration

Set your Seq API key in `secrets.json`:
```json
{
  "default": "YOUR_SEQ_API_KEY",
  "ops": "YOUR_OPS_API_KEY"
}
```

## Health Check

Visit `http://localhost:8080/healthz` to check server status.

## Metrics

Prometheus metrics available at `http://localhost:8080/metrics`.