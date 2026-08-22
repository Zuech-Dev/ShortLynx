# Inbound Conversion Reporting — Feature Plan

> **Status: SCOPING / NOT STARTED (2026-08-03).** No code written. This is Phase C of
> [CONVERSIONS_LOOP_PLAN.md](CONVERSIONS_LOOP_PLAN.md), expanded to implementation detail. Depends on
> Phase A (click correlation) existing first — there is nothing to report a conversion *against* until a
> click ID exists. Independent of [WEBHOOKS_PLAN.md](WEBHOOKS_PLAN.md) (Phase B) except at the very end,
> where a recorded conversion becomes one more thing that can trigger a `Conversion`-type webhook delivery.

Related: [CONVERSIONS_LOOP_PLAN.md](CONVERSIONS_LOOP_PLAN.md) §4 (original sketch, corrected below) ·
`Scopes.cs` (API key scopes — this needs a new one) · `LinksController`/`MeLinksController` (the
API-key-vs-session dual-controller pattern this follows).

---

## 1. Correcting the parent plan's sketch: this is API-key auth, not session auth

CONVERSIONS_LOOP_PLAN.md §4 sketched `POST /me/account`-style session auth. That's wrong once you think
through who actually calls this: **the operator's own backend**, server-to-server, after a real purchase
completes on their site. A browser session cookie makes no sense for that caller — nobody sits at a
dashboard clicking "report a conversion" the moment a customer checks out.

The existing codebase already has the right shape for this distinction — `LinksController`
(`[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]`, API-key clients) sits alongside
`MeLinksController` (`SessionControllerBase`, dashboard clients), both fronting the same service. Same
split here:

- **`POST /conversions`** (new, API-key authenticated) — the actual reporting endpoint, what an
  operator's backend integrates against.
- **`GET /me/conversions`** (new, session authenticated) — read-only, lets the dashboard show "here's what
  we've received," matching how Stripe's own dashboard is a read view over API-created objects, not the
  thing that creates them.

A new scope, `conversions:write`, joins the existing set in `Scopes.cs`
(`links:read/write`, `codes:write`, `analytics:read`, `domains:read/write`) — and the frontend's API Keys
page (currently a hardcoded checkbox list from `Scopes.All`) picks it up automatically once added there.

---

## 2. The click lookup problem

The click ID from Phase A is the PK of either `VisitEntity` (Mode 1, anonymous links) or
`UserVisitEntity` (Mode 2, user-attributed) — two separate tables with no shared base, so which one a
given ID belongs to isn't known in advance. `POST /conversions` looks up both (two indexed point queries,
not a join — cheap either way) and 404s if neither has it. No shortcut here worth the complexity of
unifying the tables just for this.

## 3. Data model

```csharp
public class ConversionEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    // Exactly one of these is set, matching whichever table the click ID resolved against.
    public Guid? VisitId { get; set; }
    public Guid? UserVisitId { get; set; }

    public required string Type { get; set; }        // free text ("purchase", "signup", ...) -- see §6
    public decimal? Value { get; set; }
    public string? Currency { get; set; }             // ISO 4217, e.g. "USD"
    public string? HashedEmail { get; set; }          // sha256 hex, caller-hashed -- see §5
    public string? HashedPhone { get; set; }
    public string? IdempotencyKey { get; set; }        // optional, operator-supplied -- see §4
    public DateTimeOffset CreatedAt { get; set; }      // when ShortLynx received it
    public DateTimeOffset? ConvertedAt { get; set; }   // when the caller says it happened, if different
}
```
`ConvertedAt` is separate from `CreatedAt` because a conversion is sometimes reported with a delay
(batch reconciliation, an async fulfillment step) — the dashboard and any downstream ad-platform push
(Phase D) should be able to show/use "when it actually happened," not just "when we heard about it."

## 4. Idempotency

Operator backends retry. A webhook-triggered "on purchase" handler that fires twice (the caller's own
retry logic, not ShortLynx's) must not record two conversions for one sale — especially once Phase D
exists and a duplicate could get forwarded to Meta/TikTok/GA4 and inflate the advertiser's own reported
ROAS, which is a real, consequential bug class, not just a cosmetic double-count.

`idempotencyKey` is optional in the request body, unique per `(AccountId, IdempotencyKey)` when
provided:
- Same key, identical payload → return the original record, 200, no new row. (Mirrors the Resend
  idempotency-key semantics already familiar from this project's own email integration.)
- Same key, different payload → 409, the caller has a bug worth surfacing, not silently swallowing.
- No key provided → always creates a new row. Fine for callers who already dedupe on their own side;
  the key is a convenience, not a requirement.

## 5. PII handling

`hashedEmail`/`hashedPhone` are optional, and **must arrive already hashed** — SHA-256 hex, lowercase,
of the normalized (trimmed + lowercased for email) value, matching what Meta CAPI/TikTok Events expect
for advanced matching so a value accepted here is directly forwardable in Phase D without
re-transformation. ShortLynx never receives, computes, or stores a raw email/phone number anywhere in
this flow — the operator's own backend already holds the real value at the moment of conversion (their
customer just checked out on their site), so hashing happens there.

**Cheap, real validation**: reject any `hashedEmail`/`hashedPhone` that isn't exactly 64 lowercase hex
characters (`^[a-f0-9]{64}$`). This can't verify the hash is *correct* — that's impossible without the
plaintext — but it does catch the actual mistake that will happen in practice: a caller accidentally
sending a raw email because they misread the API docs. A raw email fails that regex outright, so this
is real defense-in-depth, not theater.

## 6. `Type` is free text, not an enum

Deliberately — "purchase," "signup," "trial_started," "demo_booked" are all real conversion types across
different businesses, and enumerating them ahead of the actual operators using this would mean either
guessing wrong or shipping an escape hatch anyway. Free text, trimmed, length-capped (matching how
`SocialConnection`/link fields already cap length elsewhere). Phase D's platform-specific mapping (does
"purchase" map to Meta's `Purchase` standard event, or pass through as a custom event name) is that
phase's problem, not this one's.

## 7. Response shape

```json
{
  "id": "...",
  "linkId": "...",        // resolved from the click, so the caller can confirm attribution without a second lookup
  "campaignId": "...",    // null if the link isn't in a campaign
  "clickedAt": "2026-08-03T11:00:00Z",
  "convertedAt": "2026-08-03T12:30:00Z",
  "type": "purchase",
  "value": 49.00,
  "currency": "USD"
}
```
Echoing the resolved link/campaign context back is a genuine convenience, not filler — it's how the
caller confirms "yes, this landed on the click I meant" in the same round trip, without a follow-up
`GET`.

## 8. Entitlements

`PlanFeature.Conversions` (already reserved, unused) gates both `POST /conversions` and `GET
/me/conversions`. Unlike WEBHOOKS_PLAN.md's open question about click-only webhooks maybe *not* needing
this flag, there's no ambiguity here — reporting a conversion is the feature this flag is named for.

## 9. Retention interaction (flag now, resolve later)

If a click's `VisitEntity`/`UserVisitEntity` row is ever rolled up or dropped by a future retention
policy (PRIVACY_ANALYTICS_PLAN.md's still-open "keep per-visit detail briefly, then roll up" hook) before
a conversion is reported against it, the click ID stops resolving — a legitimate late conversion (a
customer who clicked, browsed, and bought a week later) would 404 with no way to attach campaign
context. Two ways to handle it, not decided here:
- Retention keeps a minimal "click existed, here's its campaign context" tombstone even after the full
  row rolls up, so late conversions can still resolve (just without the finer per-visit dimensions).
- Or: accept the loss, record the conversion with no click attribution past a certain age, document the
  window plainly (most ad platforms cap their own attribution windows at a matter of days anyway, so
  this may simply not matter in practice — worth confirming the actual per-platform limits before Phase D
  rather than assuming).

Whichever way this goes, it needs deciding *when retention is actually designed*, not now — flagging the
dependency here so it isn't rediscovered as a surprise later.

## 10. Interaction with Phase B (webhooks)

A successfully recorded conversion enqueues a `WebhookDeliveryEntity` row for every active
`Conversion`-subscribed webhook on the account, same as a click does today (once Phase B ships) —
`POST /conversions`'s own response returns before delivery happens, matching the "never make the
triggering request slower" rule already established in WEBHOOKS_PLAN.md.

## 11. Tests

- Click lookup: resolves against `VisitEntity`, resolves against `UserVisitEntity`, 404s for neither,
  never queries both tables when the first hits (cheap point-lookup, not a scan).
- Idempotency: same key + same payload → same record returned, no duplicate row; same key + different
  payload → 409; no key → always a new row.
- PII validation: well-formed 64-char hex hash accepted; a raw email string rejected; absent field is
  fine (optional).
- Entitlement gate: `PlanFeature.Conversions` disabled → 403, not a confusing 404/400.
- Response shape includes resolved `linkId`/`campaignId` correctly for both Mode 1 and Mode 2 clicks.
- Webhook fan-out: a `Conversion`-subscribed webhook gets a delivery row enqueued; a `Click`-only one
  doesn't.

## 12. Out of scope (this phase)

- Any forwarding to Meta CAPI/TikTok/GA4 — that's Phase D, and depends on this phase's stored shape but
  isn't built here.
- Fraud/dedup beyond the idempotency-key mechanism in §4 — genuine fraud detection (the same purchase
  reported through two different click IDs, say) is the operator's problem on their own side.
- Bulk/batch conversion reporting (uploading a CSV of historical conversions) — real request from some
  operators eventually, plausible, not scoped here.
