# Teardown development environment for Seq MCP Server
Write-Host "Tearing down Seq MCP Server development environment..." -ForegroundColor Yellow

# Stop and remove Seq container
$containerName = "seq-mcp-dev"

# Check if container exists
$containerExists = docker ps -a --format "{{.Names}}" | Where-Object { $_ -eq $containerName }

if ($containerExists) {
    Write-Host "Stopping Seq container..." -ForegroundColor Yellow
    docker stop $containerName 2>&1 | Out-Null
    
    Write-Host "Removing Seq container..." -ForegroundColor Yellow
    docker rm -f $containerName 2>&1 | Out-Null
    
    Write-Host "Seq development container removed successfully!" -ForegroundColor Green
} else {
    Write-Host "Seq development container not found." -ForegroundColor Yellow
}

# Clear environment variables for current session
Write-Host "`nClearing environment variables..." -ForegroundColor Yellow
Remove-Item Env:SEQ_SERVER_URL -ErrorAction SilentlyContinue
Remove-Item Env:SEQ_API_KEY -ErrorAction SilentlyContinue

# Also remove them from user level
[System.Environment]::SetEnvironmentVariable("SEQ_SERVER_URL", $null, [System.EnvironmentVariableTarget]::User)
[System.Environment]::SetEnvironmentVariable("SEQ_API_KEY", $null, [System.EnvironmentVariableTarget]::User)

Write-Host "Development environment teardown complete!" -ForegroundColor Green