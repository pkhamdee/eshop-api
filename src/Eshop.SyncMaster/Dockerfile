# eshop-sync-master. Build context: services/eshop-api (the solution root), shared Domain/Data projects.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:a9298ccc39c6258ae71228479e2f3f8d2ceb6639a9927262a4b0ff7d319a0e7e
WORKDIR /app
COPY --from=build /app .
# The base image already runs as uid 1654; stating it keeps the SAST gate (and readers) from assuming root.
USER $APP_UID
ENTRYPOINT ["dotnet", "Eshop.SyncMaster.dll"]
