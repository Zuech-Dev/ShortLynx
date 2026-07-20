#!/bin/sh
# Container entrypoint for ShortLynx.Core.
#
# When RUN_MIGRATIONS=true, apply any pending EF Core migrations before starting the API,
# using the self-contained migrations bundle baked into the image (no SDK needed at runtime).
# Set RUN_MIGRATIONS=true on exactly ONE service (Core) so migrations run once per release.
# efbundle is idempotent — it only applies migrations the database hasn't seen.
set -e

if [ "$RUN_MIGRATIONS" = "true" ]; then
  echo "[entrypoint] Applying database migrations..."
  # The bundle's design-time factory reads DATABASE_URL; --connection is the explicit target.
  # Both are set to the same keyword-form connection string the app itself uses.
  export DATABASE_URL="$Database__ConnectionString"
  ./efbundle --connection "$Database__ConnectionString"
  echo "[entrypoint] Migrations applied."
fi

# Optional GeoLite2 fetch (no-op unless MAXMIND_LICENSE_KEY + VisitSink__GeoIpDatabasePath
# are set; never fatal — the app falls back to NullGeoIpResolver when the file is absent).
./fetch-geoip.sh || true

exec dotnet ShortLynx.Core.dll
