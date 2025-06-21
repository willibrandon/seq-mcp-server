FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SeqMcpServer/SeqMcpServer.csproj", "SeqMcpServer/"]
RUN dotnet restore "SeqMcpServer/SeqMcpServer.csproj"
COPY . .
WORKDIR "/src/SeqMcpServer"
RUN dotnet build "SeqMcpServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SeqMcpServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SeqMcpServer.dll"]