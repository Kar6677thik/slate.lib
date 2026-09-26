# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props slate.lib.sln ./
COPY src/Slate.Lib.Core/Slate.Lib.Core.csproj src/Slate.Lib.Core/
COPY src/Slate.Lib.Api/Slate.Lib.Api.csproj src/Slate.Lib.Api/
RUN dotnet restore src/Slate.Lib.Api/Slate.Lib.Api.csproj
COPY src/Slate.Lib.Core/ src/Slate.Lib.Core/
COPY src/Slate.Lib.Api/ src/Slate.Lib.Api/
ARG SLATE_VERSION=0.4.0
ARG SOURCE_REVISION_ID=container
RUN dotnet publish src/Slate.Lib.Api/Slate.Lib.Api.csproj -c Release --no-restore -o /out \
    -p:SlateVersion=${SLATE_VERSION} -p:SourceRevisionId=${SOURCE_REVISION_ID} -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
RUN apt-get update \
    && apt-get install --no-install-recommends -y ca-certificates git openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
STOPSIGNAL SIGTERM
USER $APP_UID
ENTRYPOINT ["dotnet", "Slate.Lib.Api.dll"]
