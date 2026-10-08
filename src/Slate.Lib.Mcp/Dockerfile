FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json slate.lib.sln ./
COPY src/Slate.Lib.Core/Slate.Lib.Core.csproj src/Slate.Lib.Core/
COPY src/Slate.Lib.Mcp/Slate.Lib.Mcp.csproj src/Slate.Lib.Mcp/
RUN dotnet restore src/Slate.Lib.Mcp/Slate.Lib.Mcp.csproj
COPY src/Slate.Lib.Core src/Slate.Lib.Core
COPY src/Slate.Lib.Mcp src/Slate.Lib.Mcp
RUN dotnet publish src/Slate.Lib.Mcp/Slate.Lib.Mcp.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    HOME=/tmp
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Slate.Lib.Mcp.dll"]
