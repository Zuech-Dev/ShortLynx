FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShortLynx.Admin/ShortLynx.Admin.csproj", "ShortLynx.Admin/"]
RUN dotnet restore "ShortLynx.Admin/ShortLynx.Admin.csproj"
COPY . .
WORKDIR "/src/ShortLynx.Admin"
# The Tailwind CLI binary (tools/tailwindcss) is git-ignored and excluded via .dockerignore, so the
# BuildTailwind target is skipped (warning only) and the committed wwwroot/css/tailwind.css ships
# as-is. To regenerate in-container instead, fetch tailwindcss-linux-x64 into tools/ before the build.
RUN dotnet build "./ShortLynx.Admin.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShortLynx.Admin.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ShortLynx.Admin.dll"]
