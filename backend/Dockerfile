# syntax=docker/dockerfile:1

# Multi-stage build: the SDK stage runs on the target architecture, so the same
# file produces native linux/arm64 (Raspberry Pi) and linux/amd64 images.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/VerkkuGames.Domain/VerkkuGames.Domain.csproj src/VerkkuGames.Domain/
COPY src/VerkkuGames.Application/VerkkuGames.Application.csproj src/VerkkuGames.Application/
COPY src/VerkkuGames.Infrastructure/VerkkuGames.Infrastructure.csproj src/VerkkuGames.Infrastructure/
COPY src/VerkkuGames.Api/VerkkuGames.Api.csproj src/VerkkuGames.Api/

RUN dotnet restore src/VerkkuGames.Api/VerkkuGames.Api.csproj

COPY src/ src/

# Never let host build artifacts (bin/obj with Windows paths) leak into the image.
RUN rm -rf src/*/bin src/*/obj

RUN dotnet publish src/VerkkuGames.Api/VerkkuGames.Api.csproj \
    --configuration Release \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

COPY --from=build /app/publish .

# $APP_UID is provided by the official .NET images: run as the non-root "app" user.
USER $APP_UID

ENTRYPOINT ["dotnet", "VerkkuGames.Api.dll"]
