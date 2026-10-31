# eshop-sync-master. Build context: services/eshop-api (the solution root), shared Domain/Data projects.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./
COPY src/Eshop.Domain/Eshop.Domain.csproj src/Eshop.Domain/
COPY src/Eshop.Data/Eshop.Data.csproj src/Eshop.Data/
COPY src/Eshop.SyncMaster/Eshop.SyncMaster.csproj src/Eshop.SyncMaster/
RUN dotnet restore src/Eshop.SyncMaster/Eshop.SyncMaster.csproj
COPY src/ src/
RUN dotnet publish src/Eshop.SyncMaster/Eshop.SyncMaster.csproj -c Release -o /app --no-restore

# Chiseled: no shell or package manager, runs as non-root (uid 1654), listens on 8080 (health checks only).
# "-extra" adds ICU: Microsoft.Data.SqlClient does not support Globalization Invariant Mode.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
# The base image already runs as uid 1654; stating it keeps the SAST gate (and readers) from assuming root.
USER $APP_UID
ENTRYPOINT ["dotnet", "Eshop.SyncMaster.dll"]
