# Setup development environment for Seq MCP Server
Write-Host "Setting up Seq MCP Server development environment..." -ForegroundColor Green

# Check if Docker is running
try {
    docker version | Out-Null
} catch {
    Write-Host "Error: Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}

# Start Seq container for development with known admin credentials
Write-Host "Starting Seq development container..." -ForegroundColor Yellow

# Remove any existing container first
docker rm -f seq-mcp-dev 2>&1 | Out-Null

# Start with known admin credentials for development
$adminPassword = "DevPassword123!"
docker run -d `
    --name seq-mcp-dev `
    -p 15341:5341 `
    -p 18081:80 `
    -e ACCEPT_EULA=Y `
    -e SEQ_FIRSTRUN_ADMINUSERNAME=admin `
    -e SEQ_FIRSTRUN_ADMINPASSWORD=$adminPassword `
    -e SEQ_FIRSTRUN_REQUIREAUTHENTICATIONFORHTTPINGESTION=false `
    datalust/seq:latest

# Wait for Seq to be ready
Write-Host "Waiting for Seq to be ready..." -ForegroundColor Yellow
$ready = $false
$attempts = 0
$maxAttempts = 30

while (-not $ready -and $attempts -lt $maxAttempts) {
    Start-Sleep -Seconds 2
    $attempts++
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:18081/api" -UseBasicParsing -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    } catch {
        # Still waiting - this is normal during startup
        if ($attempts -eq $maxAttempts) {
            Write-Host "Error: Seq failed to become ready within 60 seconds" -ForegroundColor Red
            docker logs seq-mcp-dev
            exit 1
        }
    }
}

Write-Host "Seq is ready!" -ForegroundColor Green

# Create API key automatically using admin credentials
Write-Host "Creating development API key..." -ForegroundColor Yellow

# Wait a bit more for Seq to fully initialize
Start-Sleep -Seconds 2

# First, we need to login to get a session cookie
$adminPassword = "DevPassword123!"
$newPassword = "DevPassword456!"
$loginBody = @{
    Username = "admin"
    Password = $adminPassword
} | ConvertTo-Json

# Create a session to store cookies
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

try {
    # Try to login
    try {
        $loginResponse = Invoke-RestMethod -Method POST `
            -Uri "http://localhost:18081/api/users/login" `
            -ContentType "application/json" `
            -Body $loginBody `
            -SessionVariable session `
            -ErrorAction Stop
    } catch {
        # If password change is required, handle it
        if ($_.Exception.Response.StatusCode -eq 'OK' -or $_.ErrorDetails.Message -like '*password change*') {
            Write-Host "Password change required, updating password..." -ForegroundColor Yellow
            
            # Login with password change
            $changePasswordBody = @{
                Username = "admin"
                Password = $adminPassword
                NewPassword = $newPassword
            } | ConvertTo-Json
            
            $loginResponse = Invoke-RestMethod -Method POST `
                -Uri "http://localhost:18081/api/users/login" `
                -ContentType "application/json" `
                -Body $changePasswordBody `
                -SessionVariable session `
                -ErrorAction Stop
            
            Write-Host "Password updated successfully" -ForegroundColor Green
        } else {
            throw
        }
    }
    
    Write-Host "Successfully logged in as admin" -ForegroundColor Green
    
    # Now create the API key using the session
    $apiKeyBody = @{
        Title = "Development MCP Server"
        CanRead = $true
        CanWrite = $true
        CanIngest = $true
        CanSuppress = $true
    } | ConvertTo-Json
    
    $response = Invoke-RestMethod -Method POST `
        -Uri "http://localhost:18081/api/apikeys" `
        -ContentType "application/json" `
        -Body $apiKeyBody `
        -WebSession $session `
        -ErrorAction Stop
    
    $apiKey = $response.Token
    Write-Host "API Key created: $apiKey" -ForegroundColor Green
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Failed to create API key. Please check Seq logs." -ForegroundColor Yellow
    docker logs seq-mcp-dev | Select-Object -Last 20
    exit 1
}

# Set environment variables for current session
$env:SEQ_SERVER_URL = "http://localhost:18081"
$env:SEQ_API_KEY = $apiKey

# Also set them at the user level so they persist across sessions
[System.Environment]::SetEnvironmentVariable("SEQ_SERVER_URL", "http://localhost:18081", [System.EnvironmentVariableTarget]::User)
[System.Environment]::SetEnvironmentVariable("SEQ_API_KEY", $apiKey, [System.EnvironmentVariableTarget]::User)

# Create .env file in the project root for the application to read
$envContent = @"
SEQ_SERVER_URL=http://localhost:18081
SEQ_API_KEY=$apiKey
"@

$envPath = Join-Path (Split-Path $PSScriptRoot -Parent) ".env"
Set-Content -Path $envPath -Value $envContent -Force
Write-Host "`nCreated .env file at: $envPath" -ForegroundColor Green

Write-Host "`nEnvironment variables set:" -ForegroundColor Green
Write-Host "  SEQ_SERVER_URL: http://localhost:18081"
Write-Host "  SEQ_API_KEY: $apiKey"
Write-Host ""
Write-Host "You can now run the application with:" -ForegroundColor Cyan
Write-Host "  dotnet run --project SeqMcpServer"

Write-Host "`nSeq UI available at: http://localhost:18081" -ForegroundColor Cyan
Write-Host "Development environment setup complete!" -ForegroundColor Green