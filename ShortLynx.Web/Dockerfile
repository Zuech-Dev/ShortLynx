FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShortLynx.Web/ShortLynx.Web.csproj", "ShortLynx.Web/"]
RUN dotnet restore "ShortLynx.Web/ShortLynx.Web.csproj"
COPY . .
WORKDIR "/src/ShortLynx.Web"
RUN dotnet build "./ShortLynx.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShortLynx.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# chmod + apt need root; the base image runs as the non-root $APP_UID, so flip back afterwards.
# apt layer comes before the script COPYs so script edits don't re-download packages.
USER root
# curl is used only by the optional GeoLite2 fetch at startup (aspnet base image ships without it).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
COPY ShortLynx.Web/docker-entrypoint.sh ./docker-entrypoint.sh
COPY docker/fetch-geoip.sh ./fetch-geoip.sh
RUN chmod +x ./docker-entrypoint.sh ./fetch-geoip.sh
USER $APP_UID
ENTRYPOINT ["./docker-entrypoint.sh"]
