# syntax=docker/dockerfile:1.7

# --- build ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG GIT_COMMIT=unknown
ARG BUILD_TIME
WORKDIR /src

COPY global.json ./
COPY Kartova.slnx ./
COPY Directory.Packages.props ./
COPY NuGet.Config ./
COPY src/Kartova.SharedKernel/*.csproj src/Kartova.SharedKernel/
COPY src/Kartova.SharedKernel.AspNetCore/*.csproj src/Kartova.SharedKernel.AspNetCore/
COPY src/Kartova.SharedKernel.Postgres/*.csproj src/Kartova.SharedKernel.Postgres/
COPY src/Kartova.SharedKernel.Wolverine/*.csproj src/Kartova.SharedKernel.Wolverine/
COPY src/Kartova.Api/*.csproj src/Kartova.Api/
COPY src/Kartova.Migrator/*.csproj src/Kartova.Migrator/
COPY src/Modules/Catalog/Kartova.Catalog.Domain/*.csproj src/Modules/Catalog/Kartova.Catalog.Domain/
COPY src/Modules/Catalog/Kartova.Catalog.Application/*.csproj src/Modules/Catalog/Kartova.Catalog.Application/
COPY src/Modules/Catalog/Kartova.Catalog.Infrastructure/*.csproj src/Modules/Catalog/Kartova.Catalog.Infrastructure/
COPY src/Modules/Catalog/Kartova.Catalog.Contracts/*.csproj src/Modules/Catalog/Kartova.Catalog.Contracts/
COPY src/Modules/Organization/Kartova.Organization.Domain/*.csproj src/Modules/Organization/Kartova.Organization.Domain/
COPY src/Modules/Organization/Kartova.Organization.Application/*.csproj src/Modules/Organization/Kartova.Organization.Application/
COPY src/Modules/Organization/Kartova.Organization.Contracts/*.csproj src/Modules/Organization/Kartova.Organization.Contracts/
COPY src/Modules/Organization/Kartova.Organization.Infrastructure/*.csproj src/Modules/Organization/Kartova.Organization.Infrastructure/
COPY src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/*.csproj src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/

RUN dotnet restore src/Kartova.Api/Kartova.Api.csproj

COPY src/ src/

RUN dotnet publish src/Kartova.Api/Kartova.Api.csproj \
    -c Release \
    -o /out \
    --no-restore \
    /p:InformationalVersion="0.1.0+${GIT_COMMIT}" \
    /p:BuildTime="${BUILD_TIME}"

# --- runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -g 1000 kartova && adduser -D -u 1000 -G kartova kartova
USER kartova:kartova

COPY --from=build --chown=kartova:kartova /out ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "Kartova.Api.dll"]
