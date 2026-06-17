# ShortLynx — Deploy Checklist

**Stack:** .NET 10 · PostgreSQL · three deployable apps + external SMTP
**Target:** Railway (recommended — see platform notes) · 178 tests green at time of writing

---

## What gets deployed

| App | Role | Public? | Needs |
|---|---|---|---|
| `ShortLynx.Web` | Public redirect site (`/{code}` → 302) | **Yes** (your short domain) | DB |
| `ShortLynx.Core` | REST API (link/api-key/magic-link, analytics) | Yes (api subdomain) | DB, HmacSecret, AdminSecret, SMTP |
| `ShortLynx.Admin` | Blazor admin dashboard (magic-link auth) | Yes (admin subdomain) | DB, HmacSecret, allowlist, SMTP |
| PostgreSQL | Shared database | No | — |
| SMTP provider | Magic-link emails (Resend / Postmark / SES…) | — | Railway has none built in |

→ On Railway: **3 services + 1 Postgres**, all services referencing the same DB.

---

## Platform notes

**Railway (recommended for now).** You already run apps there, it speaks Docker, the Postgres plugin is one click, and three services + a DB is well within its model. Downsides: single-region (every redirect hits that region) and cost climbs with always-on services.

**Worth considering:**
- **Fly.io** — anycast + multi-region. The redirect path (`ShortLynx.Web`) is latency-sensitive and global; Fly can run it close to users with a read-replica or regional cache. Best upgrade path *if* redirect latency matters. More ops than Railway.
- **Azure Container Apps + Azure DB for PostgreSQL** — native .NET home, scale-to-zero, managed Postgres. Pick if you want Microsoft-ecosystem integration.
- **Render** — Railway-equivalent DX; no real advantage over staying put.

**Recommendation:** ship on Railway now. If redirect p50 latency becomes a concern later, move just `ShortLynx.Web` to Fly.io (multi-region) while keeping Core/Admin/DB on Railway.

---

## 🚧 Blocking code/config changes (do before first prod deploy)

- [ ] **Forwarded headers** — none of the apps call `UseForwardedHeaders`. Behind Railway's TLS-terminating proxy this breaks: rate-limiting + visit IPs collapse to the proxy IP, and `UseHttpsRedirection` can loop. Add `UseForwardedHeaders` (honor `X-Forwarded-For`/`X-Forwarded-Proto`) early in each pipeline. *(This is the M3 follow-up — now deploy-blocking.)*
- [ ] **Admin cookie `SecurePolicy = Always`** — set it in `AddShortLynxAuth` so the session cookie isn't sent cleartext behind the proxy. *(M4 follow-up.)*
- [ ] **Migrations strategy** (see below) — apps do **not** auto-migrate, and Core/Web/Admin don't reference the migrations assembly, so runtime `Migrate()` won't work without a project reference.
- [ ] **Admin CSS in the container** — the Linux build can't run the macOS Tailwind binary. Either commit the generated `ShortLynx.Admin/wwwroot/css/tailwind.css`, **or** have the Admin Dockerfile download `tailwindcss-linux-x64` and run it. (The build won't fail without it — you'll just ship unstyled Tailwind utilities.)
- [ ] **Dockerfiles for Admin + Web** — only `ShortLynx.Core/Dockerfile` exists. Clone it for the other two (swap the project name + `ENTRYPOINT`). Each Railway service points at its own Dockerfile.
- [ ] **Port binding** — the `aspnet:10.0` image listens on `8080` (matches `EXPOSE 8080`). Set each Railway service's target port to **8080**, or set `ASPNETCORE_HTTP_PORTS=${{PORT}}`.
- [ ] **Health checks (nice-to-have)** — add `/health` (`AddHealthChecks().MapHealthChecks("/health")`) and point Railway's healthcheck at it. Note: Core has no `/` route (would 404 a root healthcheck).

---

## Environment variables (Railway → service Variables)

ASP.NET maps nested keys with `__` (double underscore); arrays use `__0`, `__1`.

**All three services — database:**
```
Database__Provider = postgresql
Database__ConnectionString = Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};SSL Mode=Require;Trust Server Certificate=true
ASPNETCORE_ENVIRONMENT = Production
```

**Core + Admin — shared secret (identical value in both):**
```
ApiKey__HmacSecret = <32+ random chars>      # apps fail-fast on empty/default/<32
```

**Core only:**
```
ApiKey__AdminSecret      = <16+ random chars>   # gates POST /api-keys
MagicLink__ConfirmationUrlBase = https://<admin-domain>/auth/confirm
Email__Host = ...   Email__Port = 587   Email__UseSsl = true
Email__Username = ...   Email__Password = ...
Email__FromAddress = noreply@<domain>   Email__FromName = ShortLynx
```

**Admin only (fail-closed — no login without these):**
```
Admin__AllowedEmails__0   = you@example.com
Admin__SuperAdminEmails__0 = you@example.com
MagicLink__ConfirmationUrlBase = https://<admin-domain>/auth/confirm
Email__* = (same SMTP block as Core)
```

**Web only:** database block only (no secrets/SMTP).

---

## Migrations

Apps don't migrate on startup. Pick one:

- **Recommended — idempotent SQL script** applied to Railway Postgres on each migration change:
  ```bash
  dotnet ef migrations script --idempotent \
    --project ShortLynx.Data.PostgreSql --startup-project ShortLynx.Data.PostgreSql \
    -o migrate.sql
  # then run migrate.sql against the Railway DB (psql / Railway data tab)
  ```
- **Alternative — startup `Migrate()`**: add a `ProjectReference` to `ShortLynx.Data.PostgreSql` in one app and call `db.Database.Migrate()` at boot (guard so only one service does it).

---

## Pre-Deploy
- [ ] `dotnet test ShortLynx.slnx` green (currently 178)
- [ ] Working tree committed on `implementation`; pushed to origin
- [ ] **DB password rotated** (the old one was leaked — see git-scrub history) and set only in Railway variables
- [ ] All secrets above set in Railway; **no** secrets in any committed `appsettings*.json`
- [ ] Blocking changes section addressed (forwarded headers, cookie, Dockerfiles, CSS, migrations)
- [ ] Custom domains decided: short domain → Web, `admin.` → Admin, `api.` → Core

## Deploy (Railway)
- [ ] Create/confirm the Postgres service
- [ ] Apply migration script to the prod DB
- [ ] Create 3 services from the repo, each with its Dockerfile + root path; set target port 8080
- [ ] Set per-service variables; deploy Web → Core → Admin
- [ ] Attach custom domains; confirm HTTPS issued

## Post-Deploy smoke tests
- [ ] `GET https://<short-domain>/<known-code>` → 302 to the destination
- [ ] `POST https://<api>/links` with a valid API key → 201; missing scope → 403; no key → 401
- [ ] Admin: request magic link as an allowlisted email → email arrives → confirm → dashboard loads, **scoped to your data**
- [ ] Admin: non-allowlisted email cannot sign in
- [ ] Click a link, then confirm the visit appears in analytics (background writer flushed)
- [ ] Verify client IP is real (not the proxy) in a new visit row → confirms forwarded headers

## Rollback triggers
- Redirect endpoint 5xx rate > 1%, or any redirect returning 500
- Admin/Core failing to start (usually a missing/short `HmacSecret` or missing allowlist)
- DB connection failures after deploy (bad connection string / SSL mode)
- Migration applied incorrectly → restore DB snapshot, redeploy previous image

---

## First-deploy gotchas (ShortLynx-specific)
1. **App won't start** → almost always `ApiKey__HmacSecret` missing/placeholder/<32 chars (fail-fast by design).
2. **Can't log into Admin** → `Admin__AllowedEmails__0` not set (fail-closed by design).
3. **Magic-link URL points at localhost** → set `MagicLink__ConfirmationUrlBase` to the real Admin domain.
4. **All redirects share one rate-limit bucket / analytics show one IP** → forwarded headers not configured.
5. **Admin looks unstyled** → `tailwind.css` not generated for the container (commit it or fetch the Linux binary in the Dockerfile).
