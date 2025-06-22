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

# Remove any existing container and volume first
docker rm -f seq-mcp-dev 2>&1 | Out-Null
docker volume rm seq-mcp-data 2>&1 | Out-Null

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
    -v seq-mcp-data:/data `
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
    
    # Get API key template first
    Write-Host "Getting API key template..." -ForegroundColor Yellow
    $template = Invoke-RestMethod -Method GET `
        -Uri "http://localhost:18081/api/apikeys/template" `
        -WebSession $session `
        -ErrorAction Stop
    
    # Debug: Show what properties the template has
    Write-Host "Template properties: $($template.PSObject.Properties.Name -join ', ')" -ForegroundColor Gray
    
    # Modify the template - only set properties that exist
    $template.Title = "Development MCP Server"
    
    # Only set properties that exist in the template
    $properties = $template.PSObject.Properties.Name
    
    if ($properties -contains "IsEnabled") { 
        $template.IsEnabled = $true 
    }
    
    # Handle permissions - use only the permissions needed for MCP operations
    if ($properties -contains "AssignedPermissions") {
        # For newer Seq versions - only use valid permissions
        $template.AssignedPermissions = @("Ingest", "Read", "Write")
    } else {
        # For older Seq versions that use individual permission properties
        if ($properties -contains "CanRead") { $template.CanRead = $true }
        if ($properties -contains "CanWrite") { $template.CanWrite = $true }
        if ($properties -contains "CanIngest") { $template.CanIngest = $true }
        if ($properties -contains "CanSuppress") { $template.CanSuppress = $true }
    }
    
    # Create the API key from template
    Write-Host "Creating API key from template..." -ForegroundColor Yellow
    $response = Invoke-RestMethod -Method POST `
        -Uri "http://localhost:18081/api/apikeys" `
        -ContentType "application/json" `
        -Body ($template | ConvertTo-Json -Depth 10) `
        -WebSession $session `
        -ErrorAction Stop
    
    $apiKey = $response.Token
    Write-Host "API Key created: $apiKey" -ForegroundColor Green
    
    # Wait for API key to propagate
    Write-Host "Waiting for API key to activate..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    
    # Verify the API key works (with retries)
    Write-Host "Verifying API key..." -ForegroundColor Yellow
    $verified = $false
    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Seconds 2
        try {
            # Test the API key against the main API endpoint
            $testResponse = Invoke-RestMethod -Method GET `
                -Uri "http://localhost:18081/api/events?count=1" `
                -Headers @{"X-Seq-ApiKey" = $apiKey} `
                -ErrorAction Stop
                
            Write-Host "API key verified successfully!" -ForegroundColor Green
            $verified = $true
            break
        } catch {
            if ($_.Exception.Response.StatusCode -eq 'Unauthorized') {
                Write-Host "Attempt $($i+1): API key not yet active..." -ForegroundColor Yellow
            } else {
                Write-Host "Attempt $($i+1): $_" -ForegroundColor Yellow
            }
        }
    }
    
    if (-not $verified) {
        Write-Host "Error: API key verification failed after 10 attempts" -ForegroundColor Red
        Write-Host "The API key '$apiKey' is not working" -ForegroundColor Red
        Write-Host "Setup cannot continue with an invalid API key" -ForegroundColor Red
        exit 1
    }
    
} catch {
    Write-Host "Error during API key creation: $_" -ForegroundColor Red
    Write-Host "Failed to create API key. Please check Seq logs." -ForegroundColor Yellow
    docker logs seq-mcp-dev | Select-Object -Last 20
    exit 1
}

# If we got here, we have an API key - continue with setup even if verification failed

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