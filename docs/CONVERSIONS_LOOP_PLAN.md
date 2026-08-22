# Conversions Loop — Feature Plan

> **Status: SCOPING / NOT STARTED (2026-08-03).** No code written. This is Phase 3 of
> [SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md) ("Outbound: per-account webhooks; Meta CAPI /
> TikTok Events / GA4 MP click→conversion"), scoped out into its own plan because it turns out to be two
> separable features of very different size, plus one real prerequisite neither the original plan nor any
> existing code accounts for. Confirmed genuinely greenfield by direct search — no webhook infrastructure,
> no conversion entity, no click-correlation mechanism exists anywhere in this codebase today.

Related: [SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md) (parent plan, campaign/UTM model this
builds on) · [PRIVACY_ANALYTICS_PLAN.md](PRIVACY_ANALYTICS_PLAN.md) (the derive-at-ingest discipline this
must not undo) · `PlanFeature.Conversions` (`ShortLynx.Services/Entitlements/IEntitlements.cs`) — already
a reserved entitlement enum value, not yet surfaced in the pricing UI or gating anything.

---

## 1. What "conversions loop" actually requires

The plan's one sentence — "click→conversion (hashed email advanced matching available...)" — bundles
three genuinely different pieces of work:

1. **A way to correlate a later event back to a specific click.** Doesn't exist. A redirect today is a
   bare `Results.Redirect(entry.OriginalUrl, permanent: false)` — nothing about the click reaches the
   destination site at all, so there is currently no way for anyone to say "this purchase came from that
   click."
2. **A generic outbound webhook.** Also doesn't exist, and is separately the unblocked path to
   Hootsuite/Buffer already decided in SOCIAL_INTEGRATIONS_PLAN.md ("bring-your-own-key only, never
   bundled... Zapier/Make already speak Buffer/Hootsuite"). This is the higher-leverage, lower-effort half
   — one mechanism, operator wires up anything downstream.
3. **Native Meta CAPI / TikTok Events / GA4 Measurement Protocol connectors.** Three separate,
   platform-specific integrations, each with their own auth model, payload shape, and privacy
   implications. Meaningfully more work than #2, and mostly redundant with it — anything #3 would send,
   an operator can already receive via #2 and forward themselves through Zapier/Make or their own code.

**Recommendation: build #1, then #2. Treat #3 as optional, built only if operators specifically ask for
zero-setup native integrations** rather than assuming it's required — #2 alone covers the same ground for
anyone willing to do a few minutes of Zapier setup, which is exactly the bar this project already accepted
for the social-aggregator question.

---

## 2. Phase A — Click correlation (prerequisite for everything below)

### The gap
`VisitEvent`/`VisitEntity` rows get their `Id` minted in `BackgroundVisitWriter.FlushAsync` — after the
redirect has already happened, out of band, on a background writer the browser never talks to. There is
no value anywhere in the pipeline that both (a) exists at redirect time and (b) is exposed to the
destination page. Without one, a conversion reported an hour or a week later has nothing to attach to.

### The fix
Move ID generation earlier: mint the `Guid` in `ShortLynx.Web`'s redirect handler, before the 302, and
carry it through `VisitEvent` so `BackgroundVisitWriter` uses that value as the entity's PK instead of
generating a fresh one. Append it to the destination URL as a query parameter
(`?slid=<id>` — exact name TBD, namespaced to avoid colliding with the destination's own params) **only
when the destination link has conversion tracking enabled** — this is not free to turn on for every
redirect, see below.

### Why this needs to be opt-in, not default-on
An appended correlation parameter is mechanically the same shape as `gclid`/`fbclid` — a single-use,
non-identifying token, but one whose entire purpose is enabling a competitor... no, an *operator* to
stitch a click to a later event on a site ShortLynx doesn't control. That's a meaningfully different
privacy posture than everything else this product does today (which is entirely about **not** enabling
that kind of correlation). Two consequences:
- **Off by default, per-link or per-account opt-in.** A self-hoster or subscriber who never turns this on
  gets a byte-for-byte identical redirect to today.
- **Document it plainly** wherever conversion tracking is configured — this is the one feature in the
  product whose entire job is linking a click to what happened next, and that's worth being upfront about
  rather than bundling quietly into "campaign analytics."

### What doesn't change
The Phase 0.5 discipline (derive-at-ingest, discard raw UA/IP/referrer) is untouched — the correlation ID
carries no visitor information, it's an opaque reference to a row that already only holds low-entropy
derived dimensions. Nothing about this phase re-opens that question.

---

## 3. Phase B — Generic outbound webhooks (recommended first real feature)

> Expanded to full implementation detail in [WEBHOOKS_PLAN.md](WEBHOOKS_PLAN.md) — data model, delivery
> architecture, retry policy, SSRF protection, API surface, and tests. Summary below.

### Shape
- `WebhookEntity` (account-scoped): `{ Id, AccountId, Url, Secret (for HMAC signing), EventTypes[],
  IsActive, CreatedAt }`. `EventTypes` lets an operator subscribe to `click` only, `conversion` only, or
  both — most Zapier/Make use cases only care about one.
- `IWebhookDeliveryService` fired from wherever the event actually happens (a click write in
  `BackgroundVisitWriter`, a conversion report in Phase C) — **fire-and-forget from the caller's
  perspective**, same non-blocking posture as `IVisitEventSink` itself. A slow or dead webhook endpoint
  must never add latency to a redirect or an API response.
- **HMAC-signed payloads** (`X-ShortLynx-Signature` header, same convention as Stripe/GitHub) so a
  receiving endpoint can verify the request actually came from ShortLynx and wasn't forged. Secret is
  generated at webhook creation, shown once (same UX as API keys), stored hashed.
- **Retry with backoff**, capped attempts (e.g. 5, exponential), then give up — a delivery log
  (`WebhookDeliveryEntity` or similar, short retention) so an operator can see "last 20 attempts, 3
  failed, here's why" rather than a silent black hole. This is the single biggest source of support
  burden for any webhook feature and worth building in from the start, not bolted on later.
- Background delivery worker follows the exact pattern already established by
  `SocialMetricsBackgroundService`/`BackgroundVisitWriter` — nothing architecturally new here.

### Payload shape (draft)
```json
{
  "event": "conversion",
  "accountId": "...",
  "linkId": "...",
  "clickId": "...",
  "clickedAt": "2026-08-03T12:00:00Z",
  "source": "Twitter",
  "utm": { "source": "...", "medium": "...", "campaign": "..." },
  "conversion": { "type": "purchase", "value": 49.00, "currency": "USD" }
}
```
No IP, no user-agent, no PII of any kind — matches everything already established about what this
product retains at all.

---

## 4. Phase C — Inbound conversion reporting

### The endpoint
`POST /me/conversions` (session or API-key authenticated, same dual-auth pattern as the rest of `/me/*`)
— the **operator's own backend** calls this after a real conversion happens on their site, referencing
the click ID from Phase A:

```json
{ "clickId": "...", "type": "purchase", "value": 49.00, "currency": "USD",
  "hashedEmail": "sha256:...", "hashedPhone": "sha256:..." }
```

`hashedEmail`/`hashedPhone` are optional and **must already be hashed by the caller** — ShortLynx never
receives, hashes, or stores raw PII at any point in this flow. This is the load-bearing privacy
constraint of the whole feature: the operator's own backend already has the customer's email at
conversion time (they just completed a purchase on it), so hashing happens there, not here.

### Storage
A `ConversionEntity` linking back to the originating `VisitEntity`/`UserVisitEntity` by the click ID.
Gated behind `PlanFeature.Conversions` (already reserved, unused) via the existing
`IsFeatureEnabledAsync` pattern every other paid feature uses.

### What this deliberately does NOT do
Validate that a reported conversion is "real" in any way beyond basic shape/auth. Fraud/dedup is the
operator's problem on their own side; this is a reporting pipe, not a fraud-detection system.

---

## 5. Phase D — Native ad-platform connectors (optional, built on B+C)

Only worth doing if Phase B's generic webhook genuinely isn't enough for real operators — each of these
is a real, separate integration:

| Platform | API | Auth | Notes |
|---|---|---|---|
| Meta | Conversions API (CAPI) | System User access token + Pixel ID | Wants `client_ip_address`/`client_user_agent` for match quality — **we don't have either**, by design (Phase 0.5). Match quality will be visibly lower than a native pixel; say so rather than overclaim. |
| TikTok | Events API | Access Token + Pixel Code | Same IP/UA gap as Meta. |
| Google | GA4 Measurement Protocol | Measurement ID + API Secret | Wants a `client_id` — the click ID could fill this role directly, cleanest fit of the three. |

Design mirrors the existing `ISocialConnector` pattern: one `IConversionSink` per platform, resolved
per-destination, credentials stored the same shape as `SocialConnectionEntity` (encrypted, account-scoped)
minus the OAuth exchange — these are pasted long-lived tokens, not a connect flow, closer to how a Stripe
API key is entered than how Bluesky is connected.

**Be upfront in the UI that match quality is lower than a native platform pixel** — this product's whole
posture is not collecting IP/UA, and pretending otherwise to look good next to Meta's own pixel would be
the kind of overclaim this project has specifically avoided everywhere else (see the privacy policy
draft's own careful "we see briefly but never store" language, now verified true of the actual code).

---

## 6. Entitlements

`PlanFeature.Conversions` already exists and is unused — this is exactly what it's for. Gate both the
`POST /me/conversions` endpoint and webhook creation behind it via `IsFeatureEnabledAsync`, same call
shape as `SocialPublishing`/`Campaigns` today. Needs a pricing-page decision (which tier(s) get it) before
Phase B ships, not before scoping it.

## 7. Tests

- Click-ID propagation: redirect handler appends the param only when enabled on the link; absent
  otherwise; `BackgroundVisitWriter` uses the pre-minted ID, doesn't generate a second one.
- Webhook delivery: HMAC signature is correct and verifiable; retry/backoff behavior; a slow/dead
  endpoint never blocks or delays the triggering request (the actual hard requirement, not just a nice
  property — needs a test that proves it, not just an assertion in a doc comment).
- Conversion endpoint: unknown/expired click ID handled gracefully (not a 500); entitlement gate;
  malformed hashed-PII fields rejected without leaking whether a real email would have matched anything.

## 8. Open decisions

- Exact query-param name for the click ID (`slid`? something less guessable-as-a-brand?).
- Per-link vs per-account opt-in for correlation — per-link is more precise but more UI; per-account is
  simpler but coarser. Leaning per-link, matching how Mode 2/custom-domain pinning already work at the
  link level.
- Webhook delivery retention window for the delivery log (cost/debuggability trade-off, same shape as
  the still-open general retention-policy question).
- Whether Phase D ships at all, or the answer stays "use Zapier" indefinitely — genuinely undecided,
  revisit once Phase B has real usage data on whether operators ask for native integrations specifically.

## 9. Out of scope (this plan)

- Fraud/dedup detection on reported conversions.
- Any client-side JS pixel or interstitial — rejected permanently in PRIVACY_ANALYTICS_PLAN.md's
  non-goals, and nothing here reopens that.
- Retention/rollup policy for conversion or webhook-delivery rows (tracked separately, same open item as
  visit-row retention).
- Mode 1 aggregate-counter rewrite — orthogonal, tracked in PRIVACY_ANALYTICS_PLAN.md.
