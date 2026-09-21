# syntax=docker/dockerfile:1.7
# Build context is the repository root (see docker-compose.yml).

# ---- build: SDK only here ----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/Directory.Build.props services/
COPY services/TalentBridge.Search.Infrastructure/TalentBridge.Search.Infrastructure.csproj services/TalentBridge.Search.Infrastructure/
COPY services/TalentBridge.Search.Api/TalentBridge.Search.Api.csproj services/TalentBridge.Search.Api/
RUN dotnet restore services/TalentBridge.Search.Api/TalentBridge.Search.Api.csproj

COPY services/TalentBridge.Search.Infrastructure services/TalentBridge.Search.Infrastructure
COPY services/TalentBridge.Search.Api services/TalentBridge.Search.Api
# search-schema.sql is embedded into the Infrastructure assembly at build time.
COPY db/search-schema.sql db/search-schema.sql

RUN dotnet publish services/TalentBridge.Search.Api/TalentBridge.Search.Api.csproj \
      -c Release -o /app/publish --no-restore

# ---- runtime: no SDK, non-root -----------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=6 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "TalentBridge.Search.Api.dll"]
