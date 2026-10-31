# Build context: services/eshop-api (the solution root), so the shared Domain/Data projects are available.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./
COPY src/Eshop.Domain/Eshop.Domain.csproj src/Eshop.Domain/
COPY src/Eshop.Data/Eshop.Data.csproj src/Eshop.Data/
COPY src/Eshop.Api/Eshop.Api.csproj src/Eshop.Api/
RUN dotnet restore src/Eshop.Api/Eshop.Api.csproj
COPY src/ src/
RUN dotnet publish src/Eshop.Api/Eshop.Api.csproj -c Release -o /app --no-restore

# Chiseled: no shell or package manager, runs as non-root (uid 1654), listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
# The base image already runs as uid 1654; stating it keeps the SAST gate (and readers) from assuming root.
USER $APP_UID
ENTRYPOINT ["dotnet", "Eshop.Api.dll"]
