# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer-cache friendly restore)
COPY RpgGame.sln global.json ./
COPY src/RpgGame.Api/RpgGame.Api.csproj             src/RpgGame.Api/
COPY src/RpgGame.Domain/RpgGame.Domain.csproj        src/RpgGame.Domain/
COPY src/RpgGame.Infrastructure/RpgGame.Infrastructure.csproj src/RpgGame.Infrastructure/
COPY tests/RpgGame.Api.Tests/RpgGame.Api.Tests.csproj tests/RpgGame.Api.Tests/

RUN dotnet restore

# Copy source and publish
COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/RpgGame.Api/RpgGame.Api.csproj \
    -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RpgGame.Api.dll"]
