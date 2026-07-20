#!/bin/sh
# Fetches the MaxMind GeoLite2-City database at container start, enabling country/timezone
# analytics (MASTER_PLAN P1: those two dimensions only — the resolver never reads city/region).
#
# Opt-in and always non-fatal: geo resolution is an optional enhancement, so any failure here
# logs and continues — the app falls back to NullGeoIpResolver when the file is absent.
#
# Activates only when BOTH are set:
#   MAXMIND_LICENSE_KEY          — free key from a MaxMind account (GeoLite2 EULA applies)
#   VisitSink__GeoIpDatabasePath — where the app expects the .mmdb (e.g. /data/GeoLite2-City.mmdb;
#                                  point it at a mounted volume to persist across restarts)
#
# An existing file is only refreshed if older than GEOIP_MAX_AGE_DAYS (default 30 — MaxMind
# updates GeoLite2 twice weekly, but monthly is plenty for country-level buckets).

set -u

GEOIP_PATH="${VisitSink__GeoIpDatabasePath:-}"
LICENSE_KEY="${MAXMIND_LICENSE_KEY:-}"
MAX_AGE_DAYS="${GEOIP_MAX_AGE_DAYS:-30}"

[ -z "$GEOIP_PATH" ] && exit 0
if [ -z "$LICENSE_KEY" ]; then
  [ ! -f "$GEOIP_PATH" ] && echo "[geoip] VisitSink__GeoIpDatabasePath is set but the file is missing and MAXMIND_LICENSE_KEY is not set — geo resolution will be disabled."
  exit 0
fi

if [ -f "$GEOIP_PATH" ] && [ -z "$(find "$GEOIP_PATH" -mtime "+$MAX_AGE_DAYS" 2>/dev/null)" ]; then
  echo "[geoip] Database at $GEOIP_PATH is fresh (< ${MAX_AGE_DAYS}d) — skipping download."
  exit 0
fi

echo "[geoip] Fetching GeoLite2-City..."
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

if ! curl -fsSL --retry 3 --max-time 120 \
    "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-City&license_key=${LICENSE_KEY}&suffix=tar.gz" \
    -o "$TMP_DIR/geoip.tar.gz"; then
  echo "[geoip] Download failed — continuing without geo resolution."
  exit 0
fi

if ! tar -xzf "$TMP_DIR/geoip.tar.gz" -C "$TMP_DIR" 2>/dev/null; then
  echo "[geoip] Extract failed — continuing without geo resolution."
  exit 0
fi

MMDB="$(find "$TMP_DIR" -name '*.mmdb' | head -1)"
if [ -z "$MMDB" ]; then
  echo "[geoip] No .mmdb in the archive — continuing without geo resolution."
  exit 0
fi

mkdir -p "$(dirname "$GEOIP_PATH")" 2>/dev/null || true
if mv "$MMDB" "$GEOIP_PATH" 2>/dev/null; then
  echo "[geoip] Installed $(basename "$MMDB") at $GEOIP_PATH."
else
  echo "[geoip] Could not write to $GEOIP_PATH (missing volume or permissions?) — continuing without geo resolution."
fi
exit 0
