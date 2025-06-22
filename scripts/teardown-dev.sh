#!/bin/bash

# Teardown development environment for Seq MCP Server
echo -e "\033[33mTearing down Seq MCP Server development environment...\033[0m"

# Stop and remove Seq container
container_name="seq-mcp-dev"

# Check if container exists
if docker ps -a --format "{{.Names}}" | grep -q "^${container_name}$"; then
    echo -e "\033[33mStopping Seq container...\033[0m"
    docker stop "$container_name" > /dev/null
    
    echo -e "\033[33mRemoving Seq container...\033[0m"
    docker rm "$container_name" > /dev/null
    
    echo -e "\033[32mSeq development container removed successfully!\033[0m"
else
    echo -e "\033[33mSeq development container not found.\033[0m"
fi

# Note about environment variables
echo -e "\n\033[33mNote: Environment variables SEQ_SERVER_URL and SEQ_API_KEY remain set in current shell.\033[0m"
echo "To unset them, run:"
echo "  unset SEQ_SERVER_URL"
echo "  unset SEQ_API_KEY"

echo -e "\033[32mDevelopment environment teardown complete!\033[0m"