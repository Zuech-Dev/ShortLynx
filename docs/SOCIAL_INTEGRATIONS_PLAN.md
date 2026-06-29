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

## Architecture
- **`Campaign` entity** (account-scoped): groups links; per-campaign analytics roll-up; optional default
  `utm_*` template applied to destinations. This is the container "track campaigns overall" needs.
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

## Phases

### Phase 0 — Campaigns + source attribution (no external API; do first)
- `Campaign` model + `/me/campaigns` CRUD + assign links to a campaign; UTM template.
- **Source detection** from `Referrer`/`UserAgent` in the visit write path: map known wrappers/in-app UAs
  (`t.co`, `bsky.app`, `lnkd.in`, `out.reddit.com`, `l.facebook.com`, IG/Threads/Mastodon) → a `Source`
  column on the visit. "Clicks by platform" + per-campaign dashboards with zero approvals.

### Phase 1 — Tier-A publishing (Bluesky + Mastodon)
- `SocialConnection` entity + migration + encrypted token storage; connect/disconnect under `/me/social`.
- `ISocialConnector` + Bluesky (app-password→OAuth) and Mastodon connectors.
- "Create link → post to connected accounts" flow (Core endpoint + Admin UI); record `SocialPost`.
- Pull post metrics on a schedule (reuse the hosted-service pattern from domain re-verification).

### Phase 2 — Gated platforms (Threads, Reddit)
- Meta app + Tech-Provider Verification + per-permission review; Threads connector (container/publish,
  insights). Reddit app pre-approval; submit + read, respect per-subreddit rules + rate limits.

### Phase 3 — Conversions loop + Substack
- Outbound: per-account **webhooks**; **Meta CAPI / TikTok Events / GA4 MP** click→conversion (hashed
  email advanced matching available for user-attributed links — opt-in).
- Substack: pull publication RSS to auto-create tracked links; embeds. (No write API.)

## Cross-cutting constraints
- **Link-in-post fit:** publish only where links are allowed in-post (Bluesky, Mastodon, Threads, Reddit,
  FB) — **not IG feed**.
- **OAuth lifecycle:** encrypted tokens, refresh, revocation; per-platform scopes.
- **Approval timelines/cost:** Reddit + Meta gate Phase 2 (weeks of review); plan around it.
- **Privacy/compliance:** sending conversions (esp. hashed PII) needs explicit opt-in + a data-handling
  note, consistent with the existing IP-hashing posture.

## Verification (per phase)
- Connectors tested against a faked HTTP/connector interface (no live posting in CI), mirroring the
  existing `IDnsResolver`/`IEmailSender` fake pattern.
- Source-attribution unit tests over representative referrer/UA strings.
- Token storage round-trip + refresh tests; account-scoping tests on `/me/social` and `/me/campaigns`.

## Open decisions
- Token encryption: ASP.NET DataProtection (needs persisted keys — see the deploy note) vs external KMS.
- Build vs. buy for fan-out (native connectors vs. an aggregator like Ayrshare for Tier-B reach).
- Whether Phase 0 campaigns ship before or alongside Phase 1.

## Suggested first step
Phase 0 (Campaign + source attribution) — self-contained, no external dependency, and it creates the
reporting surface everything else rolls up into. Then Phase 1 (Bluesky + Mastodon) to prove the
connector/OAuth pattern on the open platforms before tackling gated review.
