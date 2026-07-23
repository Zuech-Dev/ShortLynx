# ShortLynx

**The link shortener that respects the people clicking your links.**

Self-hosted, source-available link shortener with something no other open-source
shortener offers: **per-recipient click attribution** — know *who* clicked, not just
*how many* — without a CRM, and without compromising the privacy of the people clicking.

[![License: ELv2](https://img.shields.io/badge/license-Elastic%202.0-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-ready-336791.svg)

> Self-host it for free, forever. Your data, your links, no lock-in.

<!-- SCREENSHOT: dashboard overview — drop a real image at docs/images/dashboard.png and uncomment
![ShortLynx dashboard](docs/images/dashboard.png)
-->

---

## Why ShortLynx

Most shorteners tell you a link got 400 clicks. ShortLynx can tell you *which recipient*
clicked — mint a unique code per person per destination, so email and sales tracking work
without login at click time. The closest tools that do this are CRM-locked enterprise
suites (Outreach, Salesloft, HubSpot). ShortLynx does it self-hosted, for free.

And it does it **without selling out the clicker.** Privacy isn't a bolt-on here:

- **IPs are never stored raw** — only an HMAC-SHA256 hash keyed with a secret pepper, with the
  current hour folded into the input, so the stored value rotates every hour (raw IP never touches disk).
- **Do Not Track / Global Privacy Control are honored** — the click still counts, but every
  dimension is nulled.
- **k-anonymity (k=10)** — any breakdown value seen fewer than 10 times is folded into
  "Other," in the dashboard, the API, and exports alike.
- **No sub-country geography** — country + timezone only, never region/city (that's
  fingerprinting in low-traffic contexts).
- **Exports are aggregate-only** — never a row-per-click list that could deanonymize a small
  campaign.
- **Enforced disclosure for attributed links** — if you haven't published a privacy policy,
  recipients see a ShortLynx disclosure interstitial and choose whether to be tracked.

## Features

- **Two link modes** — anonymous (one code per URL, aggregate clicks) and user-attributed
  (a unique code per recipient, per-contact attribution)
- **Privacy-preserving analytics** — clicks, unique clickers, sources, devices, timeline,
  hour-of-day, UTM capture, country + timezone (opt-in GeoIP)
- **Custom domains** — bring your own, DNS-verified, with per-link domain pinning
- **Campaigns** — group links for roll-up reporting with shared UTM templates
- **Social publishing** — post a link to Bluesky, Mastodon, Threads, or Reddit, with
  **exact per-post click attribution** (each post gets its own code — a referrer never could)
- **QR codes** — PNG or SVG for any link or recipient code
- **Passwordless auth** — magic-link sign-in; JWT (bring-your-own-frontend) or cookie sessions
- **Accounts & roles** — Owner / Admin / Member / Viewer, with a REST API for automation
- **API keys** — scoped (`links:read`, `links:write`, `analytics:read`, …) for integrations

## Architecture

Three deployable apps over one PostgreSQL database:

| Project | Role |
|---|---|
| `ShortLynx.Web` | Public redirect site (`/{code}` → 302) — the latency-sensitive hot path |
| `ShortLynx.Core` | REST API — links, analytics, auth, API keys, domains |
| `ShortLynx.Admin` | Blazor Server dashboard (magic-link auth) |
| `ShortLynx.Models` · `.Repository` · `.Services` · `.Data*` | Shared domain, data access, business logic |

The redirect path is built for volume: rate-limit → in-memory cache → 302 → async visit
event via `System.Threading.Channels` → background service batches writes. Raw signals are
reduced to low-entropy dimensions at write time and the originals discarded.

Built on **.NET 10** and **EF Core** (PostgreSQL in production, per-provider migration
projects). 710 passing tests.

## Quickstart (local development)

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and a running
**PostgreSQL** instance.

```bash
git clone https://github.com/Zuech-Dev/ShortLynx.git
cd ShortLynx

# 1. Point the API at your database (copy the example, or use user-secrets)
cp ShortLynx.Core/appsettings.Development.json.example ShortLynx.Core/appsettings.Development.json
#   …then edit the Database:ConnectionString (Username/Password) in that file.
#   (This file is git-ignored — never commit real credentials.)

# 2. Apply the schema
dotnet ef database update \
  --project ShortLynx.Data.PostgreSql --startup-project ShortLynx.Data.PostgreSql

# 3. Run the apps (separate terminals)
dotnet run --project ShortLynx.Core     # REST API  → http://localhost:5129
dotnet run --project ShortLynx.Admin    # Admin UI   → http://localhost:5201
dotnet run --project ShortLynx.Web      # Redirects  → http://localhost:5071
```

Sign in to the Admin dashboard with a magic link (in non-production, set `Email:Mode=Log`
to read the link straight from the console instead of sending email).

Build and test everything:

```bash
dotnet build ShortLynx.slnx
dotnet test  ShortLynx.slnx
```

## Deploying to production

ShortLynx ships a Dockerfile per app and deploys cleanly to **Railway** (or any Docker host
with a PostgreSQL add-on). The Core image applies EF migrations on boot when
`RUN_MIGRATIONS=true`. Full checklist — required secrets, forwarded headers, custom-domain
TLS, smoke tests — is in **[DEPLOY.md](DEPLOY.md)**.

Bringing your own frontend against the API? See **[docs/API_AUTH.md](docs/API_AUTH.md)**.

## Security

ShortLynx uses constant-time credential comparison, HMAC-keyed API-key hashing, single-use
magic links, rotating refresh tokens with reuse detection, CSRF double-submit protection, and
DB-fresh role checks on every write. To report a vulnerability, see **[SECURITY.md](SECURITY.md)**.

## License

ShortLynx is licensed under the **[Elastic License 2.0](LICENSE)** (ELv2).

- **Self-hosting is free and unrestricted** — personal, internal, or commercial internal use.
- **Offering ShortLynx as a hosted or managed service** to third parties requires a separate
  commercial license. Contact [zuechai@gmail.com](mailto:zuechai@gmail.com).
