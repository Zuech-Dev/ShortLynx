# Social Integrations & Campaign Tracking — Plan

Increase ShortLynx usage and make it a campaign hub (not just a click counter) by connecting to social
platforms. Three directions, laddered by effort/value, sequenced so each phase stands on its own.

## What already helps (don't rebuild)
- **Visit pipeline:** every redirect emits a `VisitEvent` (carries `Referrer`, `UserAgent`, `ClickedAt`,
  IP-hashed) through `IVisitEventSink` → `BackgroundVisitWriter` (Channels → batched writes). That sink is
  the natural **fan-out point** for attribution and outbound conversions.
- **Accounts/Memberships:** the home for per-account OAuth tokens (encrypted).
- **`/me/*` REST API + API keys:** campaign automation is already scriptable.
- **Links + custom domains + QR:** the assets a campaign distributes.

---

## Analytics from existing data

The visit pipeline already collects everything needed for meaningful campaign analytics without exposing
clicker identity. The goal is campaign effectiveness, not surveillance.

### Privacy posture
- **`HashedIp`** is a one-way hash — the raw IP is never persisted. It enables deduplication
  ("same device clicked twice") without enabling identification or geo-location.
- `Referrer` and `UserAgent` describe the *source and device*, not the clicker. No cookies, no
  fingerprinting, no PII beyond what the browser sends unprompted.
- Hashed email matching in Phase 3 (Meta CAPI advanced matching) is opt-in only and must be disclosed.

### Insights derivable right now (no new collection needed)

**Volume & engagement**
- Total clicks per link / per time window
- **Unique clicks** — `COUNT(DISTINCT HashedIp)` per link; a good-enough proxy for unique visitors
- Return rate — `(total − unique) / total`: how many people clicked more than once
- Click velocity: clicks per hour/day — spike vs. sustained traffic pattern

**Source attribution** (from `Referrer` domain)

| Referrer domain | Mapped source |
|---|---|
| `t.co` | Twitter / X |
| `bsky.app`, `bsky.social` | Bluesky |
| `lnkd.in`, `linkedin.com` | LinkedIn |
| `out.reddit.com`, `reddit.com` | Reddit |
| `l.facebook.com` | Facebook |
| `l.instagram.com`, `threads.net` | Instagram / Threads |
| `mastodon.*` | Mastodon |
| *(empty)* | Direct / QR scan / email client |

Empty referrer is the dominant signal for **QR code scans** and email (clients strip referrers).
Distinguishing QR from email requires separate short codes per channel — the user-attributed model already
enables this for per-recipient codes.

**Device & browser** (from `UserAgent`)
- Mobile vs. desktop vs. tablet — determines whether QR codes are the primary asset
- OS breakdown: iOS, Android, Windows, macOS, Linux
- Browser: Chrome, Safari, Firefox, Edge

**Time patterns**
- Best day/hour for audience engagement
- Time-to-first-click after campaign launch
- Click decay curve over days

**User-attributed link specifics** (per-recipient codes)
- Per-recipient click count: who engaged, who didn't
- Time-to-first-click per recipient — useful for outreach ("clicked within 1 hour")
- Zero-click recipients — re-engage list

### The insight gap social integrations fill

Current data answers *how many clicked* but not *how many saw it*. Without impressions, links can only be
compared against each other — you can't calculate true CTR.

| What you can answer | Requires |
|---|---|
| How many (unique) clicks? | Today |
| When did people click? From which platform? On which device? | Today |
| Who clicked / who didn't (per-recipient)? | Today (user-attributed links) |
| All of the above rolled up across a campaign | Phase 0 |
| What was my reach? What was my actual CTR? | Phase 1 (platform impressions API) |
| Which specific post drove which clicks? | Phase 1 (`SocialPost` entity) |
| Full funnel: impression → click → conversion | Phase 2+ |

---

## Platform reality (drives sequencing)

| Platform | Write (post links)? | Read metrics? | Auth | Access friction | Links in-post |
|---|---|---|---|---|---|
| **Bluesky** | Yes (`com.atproto.repo.createRecord`) | Yes (public) | App password → OAuth (granular scopes) | **None** | Allowed, unwrapped |
| **Mastodon** | Yes | Yes | OAuth / app token (per-instance) | **None** (per-instance) | Allowed, unwrapped |
| **Threads (Meta)** | Yes (container → publish) | Yes (views/likes/…) | Meta OAuth | Tech-Provider Verification + per-permission review (~2–4 wks) | Allowed |
| **Reddit** | Yes (submit) | Yes | OAuth 2.0 | **Pre-approval for all apps** (~2–4 wks); free ≤100 QPM, then $0.24/1k; subreddit anti-spam norms | `out.reddit.com` wrapped |
| **Facebook/Instagram (Meta)** | FB yes; **IG feed: no caption links** | Yes | Meta OAuth | App review | FB wrapped; IG bio/Stories only |
| **Substack** | **No API** | RSS only | — | — | Manual links only |

Tiers: **A (open, build first): Bluesky, Mastodon** · **B (official, gated): Threads, Reddit** ·
**C (read/manual): Substack RSS, Instagram**.

---

## Architecture
- **`Campaign` entity** (account-scoped): groups links; per-campaign analytics roll-up; optional default
  `utm_*` template applied to destinations. This is the container "track campaigns overall" needs.
- **`Source` column on `VisitEntity`/`UserVisitEntity`:** enum derived at write time from `Referrer` +
  `UserAgent`. Enables "clicks by platform" queries without re-parsing strings at read time.
- **`SocialConnection` entity** (account-scoped): `{ Platform, ExternalAccountId, Handle, encrypted
  AccessToken/RefreshToken, ExpiresAt, Scopes }`. Tokens encrypted at rest (ASP.NET DataProtection or a
  KMS); refresh handled centrally.
- **`ISocialConnector`** abstraction (one impl per platform): `PublishAsync(text, link, media?)`,
  `GetPostMetricsAsync(postId)`, capability flags (`CanPublish`, `CanReadMetrics`, `AllowsLinkInPost`).
  Connectors register by `Platform` enum; the open ones (Bluesky/Mastodon) implement first.
- **`SocialPost` entity:** links a published post back to the `Link`/`Campaign` + platform `postId`, so
  pulled metrics (impressions/likes) sit beside our click data → **CTR = clicks ÷ impressions**.
- **Outbound event fan-out:** a second `IVisitEventSink` decorator (or a subscriber off the channel) that
  delivers click events to **webhooks** and **ad conversions APIs** (Meta CAPI, TikTok Events, GA4 MP).

---

## Phases

### Phase 0 — Campaigns + source attribution (no external API; do first) — ✅ SHIPPED
- ✅ `Campaign` model + `/me/campaigns` CRUD + `PUT /me/links/{id}/campaign` assignment; UTM template
  applied to destinations at redirect time (`UtmTemplate`, non-clobbering).
- ✅ **`ClickSource` + `DeviceType` enums + `SourceDetector`** run on the visit write path
  (`BackgroundVisitWriter`): referrer host → platform, user-agent → coarse device, stored as int columns
  on `Visits`/`UserVisits`. "Clicks by platform" + per-campaign dashboards with zero approvals.
- ✅ **Analytics enrichment:** unique clicks (distinct hashed IP), first/last click, source breakdown,
  device breakdown, daily timeline on `/me/links/{id}/analytics` (and the API-key endpoint), via a shared
  in-memory `ClickAggregator`.
- ✅ **Campaign analytics endpoint:** `/me/campaigns/{id}/analytics` — rolls the above up across all the
  campaign's links (both modes) + a per-link table.
- _Deferred to a follow-up:_ browser-family breakdown; decoding in-app (`android-app://`) referrers;
  Admin UI surfacing of the new breakdowns (this phase shipped the Core API + data layer).

### Phase 1 — Tier-A publishing (Bluesky + Mastodon)
- ✅ `SocialConnection` entity + migration + encrypted token storage; connect/disconnect under `/me/social`
  and an Admin **Social** page. Tokens encrypted via a DataProtection key ring **persisted in the DB and
  shared across Core/Admin** (one ring, survives redeploys, no volume).
- ✅ `ISocialConnector` + Bluesky (app password → `createSession`, DID as external id) and Mastodon
  (`verify_credentials`, instance-qualified external id) connectors.
- ✅ "Create link → post to connected accounts" flow: `POST /me/links/{id}/publish` (per-connection
  results, stale-token refresh + retry, `SocialPost` recorded) + a "Post to social" card on the Admin
  link page. Bluesky posts carry byte-offset link facets so the URL is clickable.
- ✅ Pull post metrics on a schedule (`SocialMetricsBackgroundService`, hourly by default + startup pass;
  manual pull via `POST /me/links/{id}/posts/refresh` and a dashboard button). Stale tokens rotate via the
  shared `ConnectorTokenGuard`.
- ⚠️ **CTR surface — deferred to Phase 2 by platform reality:** neither Bluesky nor Mastodon exposes
  impressions/views in their APIs, so likes/reposts/replies are shown instead and `Impressions` stays
  null. True CTR (clicks ÷ impressions) arrives with the gated platforms (Threads reports views).

### Phase 2 — Gated platforms (Threads, Reddit)
- ✅ **Prerequisites for Meta App Review**: a real Privacy Policy (`/Privacy`) and Data Deletion
  Instructions (`/DataDeletion`) page on the Web app — both required, hosted URLs before Meta will accept
  an app-review submission. Step-by-step operator walkthrough: [META_APP_SETUP.md](META_APP_SETUP.md).
- ✅ **Threads connector**: OAuth (code → 60-day token → refresh), two-step container/publish with
  byte-accurate permalink, and insights (Threads *does* report views/impressions — the true-CTR gap the
  Phase 1 note above flags). Same `ISocialConnector` shape as Bluesky/Mastodon, plus `IOAuthSocialConnector`
  for the browser-redirect connect flow.
- ✅ **The four URLs Meta's dashboard requires**: `/social/threads/authorize` + `/social/threads/callback`
  (OAuth), `/webhooks/threads/deauthorize` + `/webhooks/threads/delete` (Meta-signed server-to-server
  callbacks, verified via `MetaSignedRequestParser`) — all live on the Admin app.
- ⬜ **Meta's own approval process** (Business Portfolio, Tech-Provider Verification, the
  `threads_basic`/`threads_content_publish`/`threads_manage_insights` permission review) — entirely on
  Meta's side; ~2–4 weeks. Walkthrough + exact Railway config: [META_APP_SETUP.md](META_APP_SETUP.md) §6-7.
- ⬜ Reddit app pre-approval; submit + read, respect per-subreddit rules + rate limits.

### Phase 3 — Conversions loop + Substack
- Outbound: per-account **webhooks**; **Meta CAPI / TikTok Events / GA4 MP** click→conversion (hashed
  email advanced matching available for user-attributed links — opt-in only, requires explicit disclosure).
- Substack: pull publication RSS to auto-create tracked links; embeds. (No write API.)

---

## Cross-cutting constraints
- **Link-in-post fit:** publish only where links are allowed in-post (Bluesky, Mastodon, Threads, Reddit,
  FB) — **not IG feed**.
- **OAuth lifecycle:** encrypted tokens, refresh, revocation; per-platform scopes.
- **Approval timelines/cost:** Reddit + Meta gate Phase 2 (weeks of review); plan around it.
- **Privacy/compliance:** the design collects the minimum needed for campaign effectiveness. Hashed IPs
  stay hashed; referrer/UA data describes sources and devices, not individuals. Sending conversions
  (hashed PII) needs explicit opt-in + a data-handling disclosure, consistent with the IP-hashing posture.

---

## Verification (per phase)
- Connectors tested against a faked HTTP/connector interface (no live posting in CI), mirroring the
  existing `IDnsResolver`/`IEmailSender` fake pattern.
- Source-attribution unit tests over a table of representative referrer/UA strings → expected `Source`.
- Unique-click query tests with known fixture data (same `HashedIp` across rows counts once).
- Token storage round-trip + refresh tests; account-scoping tests on `/me/social` and `/me/campaigns`.

---

## Open decisions
- Token encryption: ASP.NET DataProtection (needs persisted keys — see the deploy note) vs external KMS.
- Build vs. buy for fan-out (native connectors vs. an aggregator like Ayrshare for Tier-B reach).

---

## Status / next step
**Phase 0 is shipped** (Core API + data layer): campaigns, source/device attribution, enriched link
analytics, and the campaign roll-up — the reporting surface everything else rolls up into. The Admin
dashboard now surfaces these (breakdowns, campaigns CRUD, roll-up view, filterable clicks table).

Recommended next: **Phase 0.5 — privacy hardening** ([PRIVACY_ANALYTICS_PLAN.md](PRIVACY_ANALYTICS_PLAN.md)):
finish the derive-at-ingest work Phase 0 started (drop raw User-Agent/Referrer, add browser/OS/host/
language/geo, honor DNT/Sec-GPC). Do this **before Phase 1**, since Phase 1 only adds more data surface.
Then **Phase 1 (Bluesky + Mastodon)** to prove the connector/OAuth pattern on the open platforms before
tackling gated review.
