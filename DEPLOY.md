# ShortLynx — Deploy Checklist

**Stack:** .NET 10 · PostgreSQL · three deployable apps + Resend (email) · DNS (custom domains)
**Target:** Railway (recommended — see platform notes) · 324 tests green at time of writing

---

## What gets deployed

| App | Role | Public? | Needs |
|---|---|---|---|
| `ShortLynx.Web` | Public redirect site (`/{code}` → 302) | **Yes** (your short domain + any custom domains) | DB, IpHashPepper |
| `ShortLynx.Core` | REST API (links/api-keys/magic-links/domains, analytics) | Yes (api subdomain) | DB, HmacSecret, AdminSecret, Resend, IpHashPepper, DNS egress |
| `ShortLynx.Admin` | Blazor admin dashboard (magic-link auth) | Yes (admin subdomain) | DB, HmacSecret, allowlist, Resend, PublicBaseUrl, DNS egress |
| PostgreSQL | Shared database | No | — |
| Resend | Magic-link emails (HTTP API) | — | `Resend:ApiKey` (user-secret / env) |

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

## ✅ Already addressed in code (no longer blocking)

- **Forwarded headers** — all three apps call `UseForwardedHeaders` (X-Forwarded-For/Proto) so client IPs and HTTPS scheme survive the proxy. *(D1 / M3.)*
- **Admin cookie `SecurePolicy = Always`** — set in `AddShortLynxAuth`. *(D2 / M4.)*
- **Dockerfiles** — `ShortLynx.Core`, `ShortLynx.Admin`, and `ShortLynx.Web` each ship a Dockerfile. *(D3.)*
- **Prod hardening + health checks** — Core has HSTS, RFC-7807 ProblemDetails, and `/health`; Admin and Web expose `/health` too. *(D4.)*
- **Email** — uses the Resend HTTP API (`ResendEmailSender`), not SMTP. Set `Resend__ApiKey` (and a verified `Resend__FromAddress`).

## 🚧 Still to do before first prod deploy

- [ ] **Migrations** — apps do **not** auto-migrate; apply the SQL script (see Migrations below). The
  `RehomeOwnershipToAccounts` migration **backfills data** — verify it on a seeded copy before prod.
- [ ] **Set the new secrets/config** — `VisitSink__IpHashPepper` (Core + Web), `ShortLynx__PublicBaseUrl`
  (Admin), and **`Jwt__SigningKey` (Core — fail-fast; Core won't start without it)** plus
  `Cors__AllowedOrigins` if a separate frontend calls the API. See env vars below.
- [ ] **Tailwind CSS in the containers** — the Linux build can't run the committed macOS Tailwind binary, so the build uses the committed (unminified) `wwwroot/css/tailwind.css` for **both** Admin and Web. That's fine functionally; for minified CSS, fetch `tailwindcss-linux-x64` in each Dockerfile and let the build regenerate. (Bootstrap has been fully removed — Tailwind is the only stylesheet now.)
- [ ] **Custom-domain DNS + TLS** — for each custom domain a user verifies: they create the TXT record the dashboard/API shows, then point the host at `ShortLynx.Web`. Issuing TLS certs for arbitrary customer hostnames is a **platform concern** (Railway/Fly custom-domain support or a proxy in front) — not handled in app code.
- [ ] **Outbound DNS egress** — Admin (verify button) and Core (verify endpoint + re-verification job) perform outbound DNS TXT lookups; ensure egress is allowed.
- [ ] **Port binding** — the `aspnet:10.0` image listens on `8080`. Set each Railway service's target port to **8080**, or set `ASPNETCORE_HTTP_PORTS=${{PORT}}`.

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

**Core + Web — IP-hash pepper (identical value in both; keeps visit IP hashes non-reversible):**
```
VisitSink__IpHashPepper = <random secret>    # empty = unkeyed hashing (dev only)
```

**Core only:**
```
ApiKey__AdminSecret            = <16+ random chars>   # gates POST /api-keys
MagicLink__ConfirmationUrlBase = https://<admin-or-frontend>/auth/confirm   # where the magic-link points
Resend__ApiKey                 = <resend api key>
Resend__FromAddress            = noreply@<verified-domain>   Resend__FromName = ShortLynx
# Custom-domain re-verification cadence (optional; default 1440 = 24h)
CustomDomain__ReverifyIntervalMinutes = 1440

# User sessions (bring-your-own-frontend) — see docs/API_AUTH.md
Jwt__SigningKey            = <32+ random chars>      # fail-fast: Core won't start without it
Jwt__CookieSameSite        = Lax                     # None (with CookieSecure=true) for cross-site cookies
Cors__AllowedOrigins__0    = https://<your-frontend-origin>   # only for cross-origin frontends

# Email delivery mode (optional; default Resend). Use Hybrid/Log in non-prod to read magic links from logs.
Email__Mode                = Resend                  # Resend | Log | Hybrid
# Email__DeliverableDomains__0 = your-verified-domain.com    # Hybrid only
```

**Admin only (fail-closed — no login without the allowlist):**
```
Admin__AllowedEmails__0        = you@example.com
Admin__SuperAdminEmails__0     = you@example.com
MagicLink__ConfirmationUrlBase = https://<admin-domain>/auth/confirm
Resend__ApiKey                 = <resend api key>
Resend__FromAddress            = noreply@<verified-domain>   Resend__FromName = ShortLynx
ShortLynx__PublicBaseUrl       = https://<short-domain>      # builds full short URLs in the UI
```

**Web only:** database block **plus** `VisitSink__IpHashPepper` (above). No email/admin secrets.

> Optional `CustomDomain__VerificationHostLabel` / `CustomDomain__TxtValuePrefix` override the TXT record host/value shown to users; defaults (`_shortlynx-verify` / `shortlynx-verify=`) are fine.

---

## Migrations

Apps don't migrate on startup. Current migrations (PostgreSQL):
`Initial` → `AddLinkUserOwnership` → `AddUserLinkCodeRecipient` → `AddLinkCustomDomainPin` →
`AddAccountsAndMemberships` → `RehomeOwnershipToAccounts` (**data backfill** — creates an account +
Owner membership per existing user/key and re-homes resources; verify on a seeded DB) → `AddRefreshTokens`.
Pick one:

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
- [ ] `dotnet test ShortLynx.slnx` green (currently 324)
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
- [ ] Custom domain: add one (Admin or `POST /domains`), create the shown TXT record, verify → status `Verified`; point the host at Web; pin a link to it and confirm it resolves on that host but not others
- [ ] Confirm `tailwind.css` is present and the Admin/Web pages are styled (Tailwind, no Bootstrap)
- [ ] **Accounts/teams:** superuser creates an account+owner from Admin `/users`; an owner invites a member from `/members`; the invited member can sign in and is scoped to that account
- [ ] **Session API** (if using a custom frontend): `POST /auth/magic-link` → exchange the token at `/auth/session` → `GET /me` with the bearer token → `/me/links` create/list works and is account-scoped (see [docs/API_AUTH.md](docs/API_AUTH.md))

## Rollback triggers
- Redirect endpoint 5xx rate > 1%, or any redirect returning 500
- Admin/Core failing to start (usually a missing/short `HmacSecret` or missing allowlist)
- DB connection failures after deploy (bad connection string / SSL mode)
- Migration applied incorrectly → restore DB snapshot, redeploy previous image

---

## First-deploy gotchas (ShortLynx-specific)
1. **App won't start** → almost always `ApiKey__HmacSecret` (Core+Admin) or **`Jwt__SigningKey` (Core)** missing/placeholder/<32 chars (fail-fast by design).
2. **Can't log into Admin** → `Admin__AllowedEmails__0` not set (fail-closed by design).
3. **Magic-link URL points at localhost** → set `MagicLink__ConfirmationUrlBase` to the real Admin domain.
4. **All redirects share one rate-limit bucket / analytics show one IP** → forwarded headers (now configured) or check the proxy is actually forwarding.
5. **Admin/Web looks unstyled** → committed `tailwind.css` missing from the image, or a stale copy (regenerate by fetching the Linux Tailwind binary in the Dockerfile).
6. **Pinned link 404s on its custom domain** → the domain isn't `Verified` (TXT missing), DNS isn't pointed at Web yet, or the re-verification job demoted it after the TXT changed.
7. **Stored visit IPs look reversible / differ from dev** → `VisitSink__IpHashPepper` not set (or differs between Core and Web); set the same secret in both.
