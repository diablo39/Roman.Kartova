# syntax=docker/dockerfile:1.7

# --- build ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY global.json ./
COPY Kartova.slnx ./
COPY Directory.Packages.props ./
COPY NuGet.Config ./
COPY src/Kartova.SharedKernel/*.csproj src/Kartova.SharedKernel/
COPY src/Kartova.SharedKernel.AspNetCore/*.csproj src/Kartova.SharedKernel.AspNetCore/
COPY src/Kartova.SharedKernel.Postgres/*.csproj src/Kartova.SharedKernel.Postgres/
COPY src/Kartova.Migrator/*.csproj src/Kartova.Migrator/
COPY src/Modules/Catalog/Kartova.Catalog.Domain/*.csproj src/Modules/Catalog/Kartova.Catalog.Domain/
COPY src/Modules/Catalog/Kartova.Catalog.Application/*.csproj src/Modules/Catalog/Kartova.Catalog.Application/
COPY src/Modules/Catalog/Kartova.Catalog.Infrastructure/*.csproj src/Modules/Catalog/Kartova.Catalog.Infrastructure/
COPY src/Modules/Catalog/Kartova.Catalog.Contracts/*.csproj src/Modules/Catalog/Kartova.Catalog.Contracts/
COPY src/Modules/Organization/Kartova.Organization.Domain/*.csproj src/Modules/Organization/Kartova.Organization.Domain/
COPY src/Modules/Organization/Kartova.Organization.Application/*.csproj src/Modules/Organization/Kartova.Organization.Application/
COPY src/Modules/Organization/Kartova.Organization.Contracts/*.csproj src/Modules/Organization/Kartova.Organization.Contracts/
COPY src/Modules/Organization/Kartova.Organization.Infrastructure/*.csproj src/Modules/Organization/Kartova.Organization.Infrastructure/

RUN dotnet restore src/Kartova.Migrator/Kartova.Migrator.csproj

COPY src/ src/

RUN dotnet publish src/Kartova.Migrator/Kartova.Migrator.csproj \
    -c Release \
    -o /out \
    --no-restore

# --- runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -g 1000 kartova && adduser -D -u 1000 -G kartova kartova
USER kartova:kartova

COPY --from=build --chown=kartova:kartova /out ./

ENV DOTNET_RUNNING_IN_CONTAINER=true
ENTRYPOINT ["dotnet", "Kartova.Migrator.dll"]
