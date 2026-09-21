# syntax=docker/dockerfile:1.7
# Build context is the repository root (see docker-compose.yml).

# ---- build: SDK only here ----------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers cache independently of source changes.
COPY dotnet-tools.json ./
COPY services/Directory.Build.props services/
COPY services/TalentBridge.Post.Domain/TalentBridge.Post.Domain.csproj services/TalentBridge.Post.Domain/
COPY services/TalentBridge.Post.Infrastructure/TalentBridge.Post.Infrastructure.csproj services/TalentBridge.Post.Infrastructure/
COPY services/TalentBridge.Post.Api/TalentBridge.Post.Api.csproj services/TalentBridge.Post.Api/
RUN dotnet restore services/TalentBridge.Post.Api/TalentBridge.Post.Api.csproj \
 && dotnet tool restore

COPY services/TalentBridge.Post.Domain services/TalentBridge.Post.Domain
COPY services/TalentBridge.Post.Infrastructure services/TalentBridge.Post.Infrastructure
COPY services/TalentBridge.Post.Api services/TalentBridge.Post.Api
COPY db/migrations db/migrations

RUN dotnet publish services/TalentBridge.Post.Api/TalentBridge.Post.Api.csproj \
      -c Release -o /app/publish --no-restore

# A self-contained migration bundle: the compose `post-migrate` service runs
# it once against the target database, so the API itself never needs the SDK
# or Development-mode startup migrations.
RUN dotnet ef migrations bundle \
      --project services/TalentBridge.Post.Infrastructure \
      --startup-project services/TalentBridge.Post.Infrastructure \
      --configuration Release --force --output /app/efbundle

# ---- runtime: no SDK, non-root -----------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=build /app/efbundle ./efbundle

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "TalentBridge.Post.Api.dll"]
