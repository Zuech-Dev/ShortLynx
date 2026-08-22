# Native Ad-Platform Conversion Connectors — Feature Plan

> **Status: SCOPING / NOT STARTED (2026-08-03).** No code written. This is Phase D of
> [CONVERSIONS_LOOP_PLAN.md](CONVERSIONS_LOOP_PLAN.md) — the parent plan's own recommendation is not to
> build this until [WEBHOOKS_PLAN.md](WEBHOOKS_PLAN.md) (Phase B) has real usage showing operators
> specifically want zero-setup native integration over a five-minute Zapier hookup. Scoped anyway, per
> request, so the decision is informed rather than deferred out of not knowing the shape of the work.
> Meta's Conversions API details below were checked against Meta's current developer docs, not recalled
> from training — TikTok Events API and GA4 Measurement Protocol were not, and should be re-verified
> before implementation; their broad shape has been stable for years but exact field/version details
> shouldn't be trusted from memory the way Meta's now can be.

Related: [CONVERSION_REPORTING_PLAN.md](CONVERSION_REPORTING_PLAN.md) (Phase C — this phase's only input)
· [WEBHOOKS_PLAN.md](WEBHOOKS_PLAN.md) (Phase B — this phase reuses its delivery/retry machinery, see §3)
· `ISocialConnector`/`ITokenProtector` (the interface + credential-encryption pattern this mirrors).

---

## 1. Why this is the phase most worth *not* building reflexively

Meta CAPI's own `user_data` object is designed around signals this product doesn't have and won't start
collecting just to feed it: `client_ip_address`, `client_user_agent`, and Meta's own `fbc`/`fbp` cookies.
PRIVACY_ANALYTICS_PLAN.md's already-verified derive-at-ingest discipline means none of the first two ever
exist past the write path, by design — sending them to Meta would mean undoing a privacy commitment this
project has specifically kept, for the benefit of one optional integration. What's actually available to
send is narrower: the click's `event_id` (for Meta's own dedup), timestamp, coarse UTM/campaign context,
and whatever hashed email/phone the operator chose to supply at conversion-report time. **Match quality
will be visibly worse than a native Meta Pixel + CAPI setup that does send IP/UA** — worth saying plainly
in the UI if this ships, not discovering as a support ticket later.

**A genuine improvement worth calling out, if this is ever prioritized**: the redirect pipeline today
parses UTM tags out of the inbound query string and discards everything else in it (see
`BackgroundVisitWriter.ParseUtm` / `UtmParser`) — including platform click IDs like `fbclid` (Meta),
`gclid` (Google), and `ttclid` (TikTok), which arrive on the URL whenever a click actually came from a
paid ad. Capturing and storing those (opt-in, same posture as Phase A's click ID itself) would let this
phase send Meta's own `fbc`/GA4's own click-based signals back to the platform that generated them —
meaningfully better match quality than hashed PII alone, and a much smaller, more contained change than
this whole phase. **If this only gets partially built, that's the part worth doing first** — it's useful
even before Phase C's reporting endpoint exists, since it just means keeping one more thing at write time.

---

## 2. Interface

Mirrors `ISocialConnector`'s shape, but genuinely simpler — there's no OAuth handshake, no publish/refresh
cycle, no metrics pull. A conversion destination is closer to how a Stripe API key gets entered than how
Bluesky gets connected: the operator pastes a long-lived credential from their own ad account, ShortLynx
verifies it works once, and every dispatch after that is a single outbound POST with no token lifecycle
to manage.

```csharp
public sealed record ConversionPayload(
    string ClickId, DateTimeOffset ClickedAt, DateTimeOffset ConvertedAt,
    string Type, decimal? Value, string? Currency,
    string? HashedEmail, string? HashedPhone,
    string? PlatformClickId);   // fbclid/gclid/ttclid, if §1's capture ships -- null otherwise

public interface IConversionSink
{
    ConversionPlatform Platform { get; }

    /// <summary>Verifies the stored credential actually works. Called once at setup, not per dispatch.</summary>
    Task<bool> VerifyAsync(ConversionDestinationContext destination, CancellationToken ct = default);

    /// <summary>Sends one conversion event. Throws on a hard rejection (bad credential, malformed payload).</summary>
    Task SendAsync(ConversionDestinationContext destination, ConversionPayload payload, CancellationToken ct = default);
}
```

## 3. Dispatch: reuse Phase B, don't rebuild it

The tempting-but-wrong move is a second delivery/retry/logging pipeline parallel to
`WebhookDeliveryEntity`. A conversion destination dispatch has the exact same requirements a webhook
delivery does — must not block the triggering request, needs durable retry across a process restart,
needs a visible log of what was sent and what happened. **Treat a configured `ConversionDestinationEntity`
as one more delivery target Phase C's conversion-recorded event fans out to, sharing
`WebhookDeliveryEntity`'s table and worker**, distinguished by a `DestinationType` (`WebhookUrl` vs
`ConversionPlatform`) rather than building a second queue that has to independently get retry/backoff/
logging right. Same retry policy from WEBHOOKS_PLAN.md §3 applies unchanged — a platform's 5xx/timeout
backs off the same way a dead Zapier hook does.

## 4. Data model

```csharp
public class ConversionDestinationEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public ConversionPlatform Platform { get; set; }
    public required string ExternalId { get; set; }        // Meta: dataset/pixel ID. GA4: measurement ID.
    public required string EncryptedCredential { get; set; } // access token / API secret, via ITokenProtector -- same as SocialConnectionEntity's tokens
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```
Per-account, not a global operator-level credential — each subscriber sends conversions to *their own*
ad account, not ShortLynx's. Entitled behind `PlanFeature.Conversions`, same as Phase C.

## 5. Platform specifics

### Meta Conversions API — verified against current docs
- **Endpoint**: `POST https://graph.facebook.com/{API_VERSION}/{DATASET_ID}/events?access_token={TOKEN}`
  — note the access token rides the **query string**, not a header. Worth flagging as an operational
  detail: unlike every other credential this project handles, this one can end up in server/proxy access
  logs on Meta's side (nothing ShortLynx can control) and needs the same care ShortLynx already takes not
  to log query strings on *its own* side (matches `ScrubSensitiveData`'s existing query-string redaction).
- **Payload**: one event object per conversion — `event_name`, `event_time` (Unix seconds, **Meta caps
  this at 7 days in the past**, confirming the attribution-window concern CONVERSION_REPORTING_PLAN.md §9
  flagged as "worth confirming" — now confirmed, and it directly bounds how late a conversion can be
  reported and still land correctly), `action_source` (`"website"` fits this use case), `user_data`
  (hashed email/phone go here), `custom_data` (value/currency), `event_id` (set to ShortLynx's own click
  ID — this is exactly Meta's documented deduplication mechanism, and doubles as the correlator on
  ShortLynx's side too).

### TikTok Events API — broad shape only, re-verify before building
Same conceptual structure (access token + pixel code, an events array, hashed `user_data`-equivalent
fields) — TikTok's own naming and exact endpoint/version should be pulled fresh from their current docs
when this is actually scheduled, not assumed from this document.

### GA4 Measurement Protocol — broad shape only, re-verify before building
`POST` to a Google-hosted endpoint with a `measurement_id` + `api_secret` pair and a `client_id`. The
click ID is a clean fit for `client_id` here specifically — GA4's whole model is built around exactly this
kind of opaque per-visit identifier, better alignment than either Meta or TikTok get out of it. Exact
current endpoint/payload shape needs the same fresh-docs check as TikTok before implementation.

## 6. Frontend UI

A "Conversion destinations" section (own page, or a card within Settings — small enough either works):
per platform, a form for the external ID + credential, a "Verify" button that calls `IConversionSink.
VerifyAsync` synchronously (same UX reasoning as WEBHOOKS_PLAN.md's test-send endpoint — a setup step
should confirm success immediately, not queue and hope), and — once real dispatch exists — a link into
the same delivery log Phase B's webhook rows use, since they're now the same table.

## 7. Tests

- Payload mapping per platform: known input → exact expected JSON shape (a golden-file style test per
  platform keeps this honest as each API evolves independently).
- Credential encryption at rest, matching `SocialConnectionEntity`'s existing token-storage tests.
- A platform dispatch failure never touches the original `ConversionEntity` row from Phase C — recorded
  once, dispatch is purely downstream and can fail without undoing the record.
- `VerifyAsync` catches a bad credential before it's saved, not after the first real dispatch fails.

## 8. Open decisions

- **Whether to build this at all** — genuinely unresolved, gated on Phase B usage per the parent plan.
- If built, **which platform first** — Meta is the best-understood of the three right now (verified docs
  above) and most likely to be the one operators actually ask for; not a strong signal either way without
  real demand data.
- Whether §1's `fbclid`/`gclid`/`ttclid` capture ships as part of Phase A instead of waiting for this
  phase — it's useful on its own and small enough to not need to wait.
- Client-side pixel + server-side CAPI **deduplication**: an operator who already runs their own Meta
  Pixel on their site and *also* enables this would get double-counted events unless the `event_id` sent
  here matches whatever their pixel sends — something ShortLynx can't automatically guarantee, only
  document clearly as the operator's own setup responsibility.

## 9. Out of scope (this phase, if it ever ships)

- Offline conversion batch upload (Meta and others support bulk historical upload separately from
  real-time events) — a different feature, not scoped here.
- Any platform beyond the three named in the parent plan.
- Automatic pixel/CAPI deduplication — flagged above as an open decision, not solved.
