# Phase 3 — Core Services

Prerequisites: Phase 2 complete (entities, DbContext, repositories exist).

---

## Step 1: `IShortCodeGenerator`

Define in `ShortLynx.Services`:

```csharp
public interface IShortCodeGenerator
{
    string Generate(Guid linkId, Guid? userId = null);
}
```

Implement two strategies:

**`RandomBase62Generator`** (Mode 1)
- Generate N cryptographically random bytes, Base62-encode, truncate to configured length
- Code length read from `IOptions<ShortCodeOptions>`

**`HashBase62Generator`** (Mode 2)
- Input: `SHA256($"{linkId}|{userId}|{attempt}")`, Base62-encode, truncate
- Called with incrementing `attempt` on collision until unique

Register both via DI; the link creation service selects based on `Link.Mode`.

---

## Step 2: `IVisitEventSink` and background visit writer

Define in `ShortLynx.Services`:

```csharp
public interface IVisitEventSink
{
    ValueTask EnqueueAsync(VisitEvent evt, CancellationToken ct = default);
}
```

**`InMemoryVisitEventSink`** (default)
- Wraps a `Channel<VisitEvent>` (bounded, drop-oldest on overflow)
- `BackgroundVisitWriter : BackgroundService` drains the channel in batches via `IDbOperations.BulkInsertVisitsAsync`
- Batch size and drain interval configurable via `appsettings.json`

Register as `AddSingleton<IVisitEventSink, InMemoryVisitEventSink>` and `AddHostedService<BackgroundVisitWriter>`.

---

## Step 3: Cache layer

Use `IDistributedCache` (Microsoft abstraction). No custom interface needed.

Cache entry shape (JSON-serialized):
```json
{
  "OriginalUrl": "...",
  "LinkId": "...",
  "UserLinkCodeId": "...",
  "IsActive": true,
  "ExpiresAt": null
}
```

- Cache key: `shortlink:{code}`
- TTL: `min(ExpiresAt, configured max TTL)` — default 24 hours
- Invalidate on any link mutation (deactivate, delete, expiry update) by calling `IDistributedCache.RemoveAsync(key)`

For dev/SQLite: register `AddDistributedMemoryCache()`. For production: register `AddStackExchangeRedisCache(...)` behind the `Database:Provider == postgresql` branch or a dedicated `Cache:Provider` config key.

---

## Step 4: URL validation service

`IUrlValidationService` with one method: `Task<ValidationResult> ValidateAsync(string url)`.

Two checks run in sequence:

1. **SSRF guard** — parse the URL, resolve hostname, reject if IP falls in RFC-1918 / loopback / link-local ranges. No HTTP request made.
2. **Domain blocklist** — check against a local blocklist file (`blocklist.txt`, one domain per line). Configurable path via `appsettings.json`. Optional Google Safe Browsing lookup behind a feature flag.

Return `ValidationResult` with `IsValid: bool` and `Reason: string?`.

---

## Step 5: API key service

`IApiKeyService`:
- `Task<(ApiKey record, string plaintextKey)> CreateAsync(string name, string[] scopes)` — generates a random 32-byte key, computes `Prefix` (first 8 chars), stores `HMAC-SHA256(key)`, returns plaintext once
- `Task<ApiKey?> ValidateAsync(string plaintextKey)` — looks up by prefix, verifies hash

HMAC secret stored in `appsettings.json` under `ApiKeys:HmacSecret` (required, must be set by operator).

---

## Step 6: Link creation service

`ILinkService`:
- `CreateAnonymousLinkAsync(string url, ApiKey owner)` — validates URL, creates `Link`, generates `ShortCode` via `RandomBase62Generator`, persists both
- `CreateUserLinkCodesAsync(Guid linkId, IEnumerable<Guid> userIds)` — bulk-mints `UserLinkCode` rows via `HashBase62Generator`, uses `IDbOperations.BulkInsertUserLinkCodesAsync`; on `UNIQUE` violation retry with incremented attempt counter per user

---

## Verification

- Unit test `RandomBase62Generator`: output is N chars, only Base62 chars, no determinism
- Unit test `HashBase62Generator`: same inputs produce same output; different attempt produces different output
- Unit test `IUrlValidationService`: rejects `http://192.168.1.1`, `http://localhost`, `file://`, accepts valid public URLs
- Integration test: create link via `ILinkService`, read back from repo, confirm code in DB

Next: [Phase 4 — Redirect Pipeline](04-redirect-pipeline.md)
