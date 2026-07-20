#!/bin/sh
# Container entrypoint for ShortLynx.Web (the redirect site — where clicks land, so the
# primary consumer of the GeoLite2 database).
set -e

# Optional GeoLite2 fetch (no-op unless MAXMIND_LICENSE_KEY + VisitSink__GeoIpDatabasePath
# are set; never fatal — the app falls back to NullGeoIpResolver when the file is absent).
./fetch-geoip.sh || true

exec dotnet ShortLynx.Web.dll
