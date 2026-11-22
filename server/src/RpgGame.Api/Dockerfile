# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the API's project files first (layer-cache friendly restore). Only the API and the
# projects it references are restored and copied: test projects aren't part of the image.
COPY global.json ./
COPY src/RpgGame.Api/RpgGame.Api.csproj             src/RpgGame.Api/
COPY src/RpgGame.Domain/RpgGame.Domain.csproj        src/RpgGame.Domain/
COPY src/RpgGame.Infrastructure/RpgGame.Infrastructure.csproj src/RpgGame.Infrastructure/
COPY src/RpgGame.Rules/RpgGame.Rules.csproj          src/RpgGame.Rules/

RUN dotnet restore src/RpgGame.Api/RpgGame.Api.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/RpgGame.Api/RpgGame.Api.csproj \
    -c Release --no-restore -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RpgGame.Api.dll"]
