# Generic Outbound Webhooks — Feature Plan

> **Status: SCOPING / NOT STARTED (2026-08-03).** No code written. This is Phase B of
> [CONVERSIONS_LOOP_PLAN.md](CONVERSIONS_LOOP_PLAN.md), expanded to implementation detail because it's
> the recommended-first, highest-leverage piece of that plan — genuinely useful on its own even before
> Phase A (click correlation) or Phase C (conversion reporting) exist, since click events alone are
> already worth forwarding to Zapier/Make.

Related: [CONVERSIONS_LOOP_PLAN.md](CONVERSIONS_LOOP_PLAN.md) (parent plan) ·
[SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md) ("bring-your-own-key only, never bundled" —
this is the mechanism that decision actually resolves to) · `ApiKeyEntity`/`MeApiKeysController` (the
"mint a secret, show it once" pattern this reuses) · `BackgroundVisitWriter`/`SocialMetricsBackgroundService`
(the two existing background-worker patterns this draws from, and why neither is quite the right shape).

---

## 1. Data model

### `WebhookEntity` (account-scoped)
```csharp
public class WebhookEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string Url { get; set; }
    public required string SecretHash { get; set; }   // HMAC secret, hashed at rest -- same as ApiKeyEntity.KeyHash
    public required string SecretPrefix { get; set; }  // first N chars shown in the UI list, full value shown once at creation
    public WebhookEventType[] EventTypes { get; set; } = []; // stored as a delimited string or a join table -- see §8 open decisions
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserAccountId { get; set; }
}

public enum WebhookEventType { Click, Conversion }
```
Two event types to start, matching what actually exists or will exist (Phase A clicks, Phase C
conversions) — not a speculative general event bus. Extending the enum later is a small, additive change;
inventing five event types nobody asked for yet is not.

### `WebhookDeliveryEntity` (the retry queue *and* the audit log — same table, not two)
```csharp
public class WebhookDeliveryEntity
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public WebhookEventType EventType { get; set; }
    public string PayloadJson { get; set; } = null!;   // the exact body sent, for replay/debugging
    public DeliveryStatus Status { get; set; }         // Pending, Delivered, Failed, Abandoned
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; } // null once Delivered/Abandoned
    public int? LastResponseStatus { get; set; }
    public string? LastError { get; set; }             // timeout/DNS/connection-refused message, truncated
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
```

**Why this is a table, not an in-memory channel like `BackgroundVisitWriter`'s.** Visit writes are
deliberately best-effort — `BoundedChannelFullMode.DropOldest`, no retry, a dropped click is an acceptable
loss under load. A webhook delivery that fails because an operator's Zapier hook was down for five minutes
is *not* acceptable to just drop — the whole point is reliable forwarding. That means retry state has to
survive a process restart, which an in-memory channel can't do. This is architecturally closer to
`SocialMetricsBackgroundService`'s poll-on-an-interval pattern than to `BackgroundVisitWriter`'s channel.

---

## 2. Delivery architecture

1. **Enqueue**: whatever produces the event (a click write in `BackgroundVisitWriter`, a conversion report
   in the Phase C endpoint) inserts a `WebhookDeliveryEntity` row per matching active webhook, status
   `Pending`, `NextAttemptAt = now`. This insert must be cheap and must never make the triggering
   request slower — a redirect or an API call is not allowed to wait on webhook fan-out.
2. **Worker**: a `BackgroundService` (same shape as `SocialMetricsBackgroundService`) polls every few
   seconds for `Pending` rows where `NextAttemptAt <= now`, claims a batch (`UPDATE ... WHERE Status =
   'Pending' ... RETURNING`, or an equivalent claim pattern — needs to be safe if this ever runs with more
   than one instance, which it will on Railway during a deploy overlap), and delivers them concurrently
   with a bounded degree of parallelism.
3. **HTTP call**: POST the payload, a short timeout (e.g. 10s — this is a fire-and-forget notification, not
   an API a receiver is meant to do real work synchronously inside), read only the status code.
4. **Outcome**: 2xx → `Delivered`. Anything else → see retry policy below.

---

## 3. Retry policy

| Response | Treatment |
|---|---|
| 2xx | `Delivered`, done. |
| 4xx (except 429) | **No retry.** A 400/404/401 means the endpoint will never accept this payload — retrying is pure noise against the operator's own server. Mark `Failed` immediately. |
| 429 | Retry, honoring `Retry-After` if present, otherwise the normal backoff. |
| 5xx, timeout, connection error, DNS failure | Retry with backoff: 1m, 5m, 30m, 2h, 12h — 5 attempts total, then `Abandoned`. |

`Abandoned` deliveries are visible in the delivery log (§5) so an operator can see "this has been failing
for 12 hours" rather than silently losing events — the single biggest source of support burden for any
webhook feature, worth designing for from day one rather than adding after the first confused ticket.

---

## 4. Security

### Signing
HMAC-SHA256 over `{timestamp}.{body}` (timestamp included in the signed content, not just the header, to
prevent a captured payload+signature being replayed later) — same construction Stripe uses, for the same
reason: a receiver can reject anything older than a few minutes even if the signature is otherwise valid.

```
X-ShortLynx-Timestamp: 1735689600
X-ShortLynx-Signature: sha256=<hex hmac of "1735689600.<raw body>">
```

Secret is generated at webhook creation (`openssl rand`-equivalent, 32+ bytes), shown once in the create
response, stored **hashed** (`SecretHash`, same treatment as `ApiKeyEntity.KeyHash` — if the database
leaks, webhook secrets don't leak with it). A displayed prefix (`SecretPrefix`, first 8 chars) lets the UI
show "which secret is this" in the list view without ever re-displaying the full value, mirroring
`ApiKeyEntity.Prefix`.

### SSRF — the one genuinely dangerous part of this feature
A webhook URL is attacker-controllable input that this server will fetch. Left unguarded, an operator (or
anyone who compromises an operator's session) could point a webhook at `http://169.254.169.254/...`
(cloud metadata), `http://postgres.railway.internal:5432`, or any other address only reachable from
*inside* the hosting network. Two checks, not one:

1. **At creation time**: resolve the hostname, reject if any resolved address is loopback, link-local,
   private-range (RFC 1918 / ULA), or a known cloud-metadata address. Reject non-`http(s)` schemes and
   URLs carrying embedded credentials (`http://user:pass@host`).
2. **At delivery time, again**: creation-time validation alone is beaten by DNS rebinding — register a
   domain that resolves to a public IP when the webhook is created, then repoints to an internal IP by
   the time delivery actually happens. The delivery `HttpClient` needs a `SocketsHttpHandler.ConnectCallback`
   that validates the IP it's about to connect to, not just the hostname string, so the check happens at
   the actual connection, immune to a DNS answer changing in between.

This is the one part of this plan that isn't "wire up a CRUD resource" — it needs to be right, and it's
worth a dedicated test file rather than a couple of assertions bolted onto delivery tests.

### Payload contents
No PII, no raw IP/UA, no auth tokens or internal IDs beyond what the operator already has (their own
`accountId`/`linkId`). Same posture as every payload shape already established in
CONVERSIONS_LOOP_PLAN.md §3.

---

## 5. API surface

```
GET    /me/webhooks              list (secret never included, only SecretPrefix)
POST   /me/webhooks              create -- { url, eventTypes } -> returns the secret once
DELETE /me/webhooks/{id}         revoke
POST   /me/webhooks/{id}/test    send a synthetic test payload, return the delivery outcome inline
                                  (not queued -- this one call should be synchronous so the UI can show
                                  "it worked" or the real error immediately, same UX reasoning as why a
                                  "test connection" button anywhere is synchronous)
GET    /me/webhooks/{id}/deliveries   recent delivery log (paginated), for debugging a failing endpoint
```
`RequireAccountAction(AccountAction.ManageResources)` on the writes, matching `MeApiKeysController` — a
webhook is a resource a Member can manage, not an account-level (`ManageAccount`) concern.

---

## 6. Frontend UI

A `/webhooks` page, same shape as `/api-keys`: list with create/revoke, secret shown once in a
copy-and-you're-done modal, event-type checkboxes at creation. Each row expands (or links) to its
delivery log — status, timestamp, response code, a "resend" affordance for anything `Failed`/`Abandoned`
(re-enqueues as a fresh `Pending` delivery rather than mutating the historical row, so the log stays an
honest record of what actually happened).

---

## 7. Entitlements

Open question, not a given: should webhook creation require `PlanFeature.Conversions`, or should
click-only webhooks be available more broadly (e.g. every tier), with the *conversion* event type
specifically gated? Leaning toward the latter — a webhook that only ever fires on `Click` doesn't depend
on anything Conversions-specific, and gating the whole feature behind a flag named for a capability it
doesn't strictly need would be confusing. Needs a pricing decision, not just an engineering one.

---

## 8. Tests

- Signing: signature verifies against a known vector; a tampered body or stale timestamp fails
  verification (write the reference receiver-side verification snippet into the docs too, so an operator
  building against this doesn't have to guess the construction).
- Retry policy: 4xx (non-429) never retries; 5xx/timeout follows the exact backoff schedule; `Abandoned`
  after 5 attempts.
- **SSRF**: dedicated test file — private/loopback/link-local IPs rejected at creation; a DNS-rebinding
  simulation (resolve to a public IP first, then flip to a private one) is caught at delivery time, not
  just creation time.
- Delivery never blocks the triggering request — a test that proves this with a deliberately slow
  webhook target, not just an assertion in a comment.
- Concurrent worker instances don't double-deliver the same row (the claim query is actually safe under
  concurrency, not just believed to be).

## 9. Open decisions

- `EventTypes` storage: delimited string column (matches `ApiKeyEntity.Scopes`' existing precedent) vs a
  join table. Leaning delimited string for consistency with the one precedent that already exists.
- Entitlement gating shape (§7).
- Whether HTTPS-only is enforced, or self-hosters may point at their own internal HTTP receiver they
  control. Leaning HTTPS-only for the hosted product, configurable for self-hosters.
- Delivery log retention window — same open question as visit-row retention generally, not resolved here.
- Worker poll interval and concurrency cap — needs real numbers, not guessed ones; revisit once there's
  usage to size against.

## 10. Out of scope (this phase)

- A general event bus beyond `Click`/`Conversion` — extend the enum later if a real use case shows up.
- Payload transformation/filtering (e.g. "only fire if value > $50") — Zapier/Make already do this
  downstream; duplicating it here is scope creep against the "bring your own tooling" posture.
- Webhook-level rate limiting beyond the retry backoff itself (protecting ShortLynx's own delivery worker
  from one account registering many webhooks) — worth a look once Phase A/B are live and there's real
  traffic to reason about, not before.
