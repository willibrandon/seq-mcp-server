#!/bin/bash

# Setup development environment for Seq MCP Server
echo -e "\033[32mSetting up Seq MCP Server development environment...\033[0m"

# Check if Docker is running
if ! docker version &> /dev/null; then
    echo -e "\033[31mError: Docker is not running. Please start Docker.\033[0m"
    exit 1
fi

# Start Seq container for development with known admin credentials
echo -e "\033[33mStarting Seq development container...\033[0m"

# Remove any existing container first
docker rm -f seq-mcp-dev 2>&1 > /dev/null

# Start with known admin credentials for development
admin_password="DevPassword123!"
docker run -d \
    --name seq-mcp-dev \
    -p 5341:5341 \
    -p 8081:80 \
    -e ACCEPT_EULA=Y \
    -e SEQ_FIRSTRUN_ADMINUSERNAME=admin \
    -e SEQ_FIRSTRUN_ADMINPASSWORD="$admin_password" \
    -e SEQ_FIRSTRUN_REQUIREAUTHENTICATIONFORHTTPINGESTION=false \
    datalust/seq:latest

# Wait for Seq to be ready
echo -e "\033[33mWaiting for Seq to be ready...\033[0m"
ready=false
attempts=0
max_attempts=30

while [ "$ready" = false ] && [ $attempts -lt $max_attempts ]; do
    sleep 2
    ((attempts++))
    if curl -s -o /dev/null -w "%{http_code}" http://localhost:5341/api | grep -q "200"; then
        ready=true
    fi
done

if [ "$ready" = false ]; then
    echo -e "\033[31mError: Seq failed to start within 60 seconds\033[0m"
    docker logs seq-mcp-dev
    exit 1
fi

echo -e "\033[32mSeq is ready!\033[0m"

# Create API key automatically using admin credentials
echo -e "\033[33mCreating development API key...\033[0m"

# Wait a bit more for Seq to fully initialize
sleep 2

# Create API key using admin credentials
admin_password="DevPassword123!"
auth_header=$(echo -n "admin:$admin_password" | base64)

api_key_response=$(curl -s -X POST http://localhost:5341/api/apikeys \
    -H "Authorization: Basic $auth_header" \
    -H "Content-Type: application/json" \
    -d '{
        "Title":"Development MCP Server",
        "CanRead":true,
        "CanWrite":true,
        "CanIngest":true,
        "CanSuppress":true
    }')

api_key=$(echo "$api_key_response" | grep -o '"Token":"[^"]*' | sed 's/"Token":"//')

if [ -z "$api_key" ]; then
    echo -e "\033[31mFailed to create API key with authentication. Trying without...\033[0m"
    
    # Try without authentication since we disabled it for ingestion
    api_key_response=$(curl -s -X POST http://localhost:5341/api/apikeys \
        -H "Content-Type: application/json" \
        -d '{
            "Title":"Development MCP Server",
            "CanRead":true,
            "CanWrite":true,
            "CanIngest":true,
            "CanSuppress":true
        }')
    
    api_key=$(echo "$api_key_response" | grep -o '"Token":"[^"]*' | sed 's/"Token":"//')
fi

if [ -z "$api_key" ]; then
    echo -e "\033[31mFailed to create API key. Using hardcoded development key.\033[0m"
    api_key="development-key-12345"
fi

echo -e "\033[32mAPI Key created: $api_key\033[0m"

# Set environment variables
export SEQ_SERVER_URL="http://localhost:5341"
export SEQ_API_KEY="$api_key"

echo -e "\n\033[32mEnvironment variables set for current session:\033[0m"
echo "  SEQ_SERVER_URL: $SEQ_SERVER_URL"
echo "  SEQ_API_KEY: $SEQ_API_KEY"

echo -e "\n\033[33mTo persist these environment variables, add to your shell profile:\033[0m"
echo "  export SEQ_SERVER_URL='http://localhost:5341'"
echo "  export SEQ_API_KEY='$api_key'"

echo -e "\n\033[36mSeq UI available at: http://localhost:8081\033[0m"
echo -e "\033[32mDevelopment environment setup complete!\033[0m"