FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShortLynx.Core/ShortLynx.Core.csproj", "ShortLynx.Core/"]
RUN dotnet restore "ShortLynx.Core/ShortLynx.Core.csproj"
COPY . .
WORKDIR "/src/ShortLynx.Core"
RUN dotnet build "./ShortLynx.Core.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShortLynx.Core.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Build a self-contained EF Core migrations bundle so migrations can be applied at startup
# without shipping the SDK in the runtime image. efbundle reads --connection at runtime and
# applies only pending migrations (idempotent). RID must match the runtime container (linux-x64).
FROM build AS migrations
WORKDIR /src
RUN dotnet tool install --global dotnet-ef --version 10.0.7
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
      --project ShortLynx.Data.PostgreSql \
      --startup-project ShortLynx.Data.PostgreSql \
      --configuration $BUILD_CONFIGURATION --self-contained -r linux-x64 \
      -o /app/efbundle

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=migrations /app/efbundle ./efbundle
# chmod + apt need root; the base image runs as the non-root $APP_UID, so flip back afterwards.
# apt layer comes before the script COPYs so script edits don't re-download packages.
USER root
# curl is used only by the optional GeoLite2 fetch at startup (aspnet base image ships without it).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
COPY ShortLynx.Core/docker-entrypoint.sh ./docker-entrypoint.sh
COPY docker/fetch-geoip.sh ./fetch-geoip.sh
RUN chmod +x ./efbundle ./docker-entrypoint.sh ./fetch-geoip.sh
USER $APP_UID
ENTRYPOINT ["./docker-entrypoint.sh"]
