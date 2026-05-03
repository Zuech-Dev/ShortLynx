# Phase 8 — Operations

Prerequisites: All prior phases complete. The app runs end-to-end.

---

## Step 1: Docker images

Add a `Dockerfile` for each host project (`ShortLynx.Core`, `ShortLynx.Web`, `ShortLynx.Admin`). Use a multi-stage build pattern:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ShortLynx.Web/ShortLynx.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ShortLynx.Web.dll"]
```

---

## Step 2: `docker-compose.yml`

Define services:
- `shortlynx-web` (ShortLynx.Web, port 8080)
- `shortlynx-core` (ShortLynx.Core, port 8081, internal only)
- `shortlynx-admin` (ShortLynx.Admin, port 8082)
- `postgres` (postgres:17-alpine, persistent volume)
- `redis` (redis:7-alpine, auth enabled, persistent volume)

Pass all config via environment variables (mapped from `appsettings.json` keys using .NET's `__` separator convention, e.g., `Database__Provider=PostgreSQL`).

Include a `migrations` init container that runs `dotnet ef database update` before the app containers start.

---

## Step 3: Data retention job

Add `DataRetentionService : BackgroundService` in `ShortLynx.Core` (or `ShortLynx.Web`):

- Runs on a configurable cron schedule (default: daily at 02:00 UTC)
- Deletes `Visit` rows where `ClickedAt < UtcNow - RetentionDays`
- Deletes `UserVisit` rows on the same policy
- Retention period read from `Retention:VisitEventDays` (default 90)
- Logs summary of deleted row counts via Serilog

Use `EF Core ExecuteDeleteAsync` (EF 7+) for efficient bulk deletes without loading rows into memory.

---

## Step 4: Structured logging

Serilog is already added to `ShortLynx.Core`. Apply to all host projects:

- Console sink: human-readable in dev, JSON in production (detect via `ASPNETCORE_ENVIRONMENT`)
- Minimum level: `Information` in production, `Debug` in development
- Enrich with: `MachineName`, `ThreadId`, `SourceContext`
- Log redirect outcomes (`code`, `mode`, `status_code`) at `Information` level — no raw IPs, no destination URLs in logs (privacy)
- Log visit write batches at `Debug` level (batch size, duration)

---

## Step 5: Health checks

Extend the `/health` endpoint (defined in Phase 7, Step 4) with named checks:

- `db`: `EF Core CanConnectAsync()`
- `cache`: `IDistributedCache.GetAsync("health-probe")`
- `visit-channel`: report `Channel.Reader.Count` as a metric (alert if backlog > threshold)

Use `Microsoft.Extensions.Diagnostics.HealthChecks`. Expose:
- `GET /health` → `200` if all healthy, `503` if any degraded/unhealthy
- `GET /health/detail` → JSON breakdown (admin-only, behind IP allowlist or API key)

---

## Step 6: Configuration reference

Document all `appsettings.json` keys in a `CONFIGURATION.md` file at the repo root:

| Key | Default | Description |
|---|---|---|
| `Database:Provider` | — | `postgresql` or `sqlite` (required) |
| `Database:ConnectionString` | — | Provider connection string (required) |
| `Cache:Provider` | `memory` | `memory` or `redis` |
| `Cache:Redis:ConnectionString` | — | Required if `Cache:Provider = redis` |
| `ShortCode:Length` | `8` | Character length of generated codes |
| `RateLimit:Redirect:RequestsPerWindow` | `60` | Requests per window per IP |
| `RateLimit:Redirect:WindowSeconds` | `60` | Window duration in seconds |
| `Retention:VisitEventDays` | `90` | Days to retain visit records |
| `Redirect:Interstitial` | `false` | Show interstitial page before redirect |
| `Redirect:DeduplicationWindowMinutes` | `30` | Minutes to deduplicate clicks per (code, IP) pair; 0 = disabled |
| `ApiKeys:HmacSecret` | — | HMAC secret for API key hashing (required) |

---

## Verification

1. `docker compose up` → all services start, migrations run, no errors
2. `curl http://localhost:8080/{code}` → `302`
3. `curl http://localhost:8081/links` with valid API key → `200`
4. `curl http://localhost:8080/health` → `200` with all checks healthy
5. Stop Redis → `/health` returns `503`, redirect still works (falls back to DB lookup)
6. Advance system clock 91 days → retention job deletes old visit rows
