# syntax=docker/dockerfile:1
#
# API image:  docker build -t vinybisol/bills-backend:local .
#
# Base images are pinned by tag + digest (ADR 20). The application version comes only from the
# build (ADR 19): pass --build-arg VERSION=X.Y.Z; without it the image reports 0.0.0-local.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
WORKDIR /src

# Restore in its own layer: it is only invalidated when a project or package file changes.
COPY global.json nuget.config Directory.Packages.props ./
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Data/Data.csproj src/Data/
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/ src/

ARG VERSION=0.0.0-local
RUN dotnet publish src/Api/Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false \
    -p:Version=${VERSION}

# ---------- final ----------
# Chiseled: distroless Ubuntu (no shell, no package manager), non-root by default. No ICU/tzdata:
# globalization is invariant. Switch to the -extra variant if time zones/cultures are needed.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-resolute-chiseled@sha256:d70c784fef20c2427f9620d75a0951b4b73e0c48dcef03ab25d34929a175b394 AS final
WORKDIR /app

ARG VERSION=0.0.0-local
LABEL org.opencontainers.image.title="bills-backend" \
      org.opencontainers.image.source="https://github.com/vinybisol/bills-backend" \
      org.opencontainers.image.version="${VERSION}"

COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]