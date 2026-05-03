# Phase 4 — Redirect Pipeline

Prerequisites: Phase 3 complete (cache layer and `IVisitEventSink` registered).

This is the hot path. Every design decision here is about keeping the p99 latency minimal.

---

## Step 1: Rate limiting middleware

Use ASP.NET Core's built-in `AddRateLimiter` (available since .NET 7).

- Policy name: `redirect`
- Algorithm: fixed window, keyed by `HttpContext.Connection.RemoteIpAddress`
- Defaults: 60 requests / 1 minute window, configurable via `appsettings.json` under `RateLimit:Redirect`
- Response on exceeded: `429 Too Many Requests` with `Retry-After` header

Register in `Program.cs`, apply only to the redirect route.

---

## Step 2: Redirect endpoint

Add a minimal API endpoint (or controller action) in `ShortLynx.Web`:

```
GET /{code}
```

Pipeline steps (in order, see DESIGN.md diagram):

1. **Rate limit check** — handled by middleware from Step 1
2. **Cache lookup** — `IDistributedCache.GetStringAsync($"shortlink:{code}")`
   - Hit: deserialize cached entry
   - Miss: query `IShortCodeRepository` or `IUserLinkCodeRepository` by code, populate cache
3. **Validity check** — if not found, inactive, or past `ExpiresAt`: return `404` (or `410` if `IsOneTimeUse && IsUsed`)
4. **Return `302 Found`** — set `Location` header to `OriginalUrl`, flush response
5. **Deduplication check** — before enqueuing, call `IDistributedCache.GetStringAsync($"dedup:{code}:{rawIp}")`. If present, skip enqueue (duplicate click within window). If absent, set with 30-minute sliding expiry (`Redirect:DeduplicationWindowMinutes: 30`).
6. **Enqueue visit event** — call `IVisitEventSink.EnqueueAsync(new VisitEvent { ... })` after the response is sent (use `HttpContext.Response.OnCompleted`)

The `VisitEvent` payload:
```csharp
record VisitEvent(
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId,           // denormalized from UserLinkCode
    string RawIp,
    string? Referrer,
    string? UserAgent,
    DateTimeOffset ClickedAt
);
```

IP hashing (rotating salt) is done inside `BackgroundVisitWriter`, not here.

---

## Step 3: Background visit writer

`BackgroundVisitWriter : BackgroundService` (registered in Phase 3 Step 2).

Processing loop:
1. Await items from `Channel<VisitEvent>` with a timeout (e.g., 500 ms) so it flushes even when traffic is low
2. Accumulate a batch up to a configured max size (e.g., 100)
3. Hash IPs: `HMAC-SHA256(rawIp, rotatingHourlySalt)` — salt derived from `$"salt-{DateTime.UtcNow:yyyyMMddHH}"`
4. Split batch into `Visit` records (Mode 1) and `UserVisit` records (Mode 2) based on which ID is set
5. Call `IDbOperations.BulkInsertVisitsAsync` and `IDbOperations.BulkInsertUserVisitsAsync`
6. On one-time-use codes: call repository to set `IsUsed = true` and invalidate cache for those codes

---

## Step 4: One-time-use code handling

After a one-time-use code is redeemed:
1. Background writer sets `UserLinkCode.IsUsed = true` via repository
2. Background writer calls `IDistributedCache.RemoveAsync($"shortlink:{code}")`
3. Subsequent requests hit the DB, find `IsUsed = true`, return `410 Gone`
4. Optionally re-cache the `410` state with a short TTL to avoid repeated DB hits

---

## Step 5: Interstitial page (optional)

If `Redirect:Interstitial: true` in config:
- Return a Razor Page instead of a direct `302`
- Page shows the destination URL and a "Continue" button (or auto-redirects after N seconds)
- The redirect step happens client-side; the visit event is enqueued server-side when the interstitial is served

---

## Verification

1. `GET /{validCode}` → `302` with correct `Location`, response time < 50 ms (cache hit)
2. `GET /{invalidCode}` → `404`
3. Flood with 61 requests/minute from same IP → 61st returns `429`
4. After redirect, confirm visit row in DB (wait for background writer to flush)
5. One-time-use: second request returns `410`
6. Restart app mid-traffic → visits in-flight at crash are lost (expected, documented tradeoff)

Next: [Phase 5 — Link Management API](05-link-management-api.md)
