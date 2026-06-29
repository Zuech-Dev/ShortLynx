# Phase 5 — Link Management API

Prerequisites: Phase 3 complete (services exist), Phase 4 complete (redirect works).

All endpoints in `ShortLynx.Core`. All require API key authentication.

**Tenancy model (Q15):** ShortLynx is designed for single-operator self-hosting on a single domain. API keys act as the owner/tenant identifier — a link belongs to the key that created it — but there is no multi-tenant isolation UI or tenant management surface. The API key model is intentionally simple: one operator, one domain, one or more API keys for programmatic access.

---

## Step 1: API key authentication middleware

Create `ApiKeyAuthenticationHandler` implementing `IAuthenticationHandler`.

- Reads `X-Api-Key` header
- Extracts prefix (first 8 chars), calls `IApiKeyService.ValidateAsync`
- On success: sets `ClaimsPrincipal` with `ApiKeyId` and `Scopes` claims
- On failure: returns `401 Unauthorized`

Register via `AddAuthentication("ApiKey").AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(...)`.

Apply to all routes in `ShortLynx.Core` except health checks.

---

## Step 2: Link endpoints

### `POST /links`
Create an anonymous (Mode 1) short link.

Request body:
```json
{ "url": "https://example.com", "expiresAt": null }
```

Response `201 Created`:
```json
{ "id": "...", "code": "abc1234", "shortUrl": "https://yourdomain.com/abc1234" }
```

Steps: validate URL → call `ILinkService.CreateAnonymousLinkAsync` → return result.

---

### `GET /links/{id}`
Return link metadata + aggregate click count.

---

### `PATCH /links/{id}`
Update `IsActive` or `ExpiresAt`. Invalidate cache for the link's code.

---

### `DELETE /links/{id}`
Soft-delete (set `IsActive = false`). Invalidate cache.

---

## Step 3: Mode 2 endpoints

### `POST /links/{linkId}/codes`
Bulk-generate user-attributed codes.

Request body:
```json
{ "userIds": ["uuid1", "uuid2", ...] }
```

Response `200 OK`:
```json
{
  "codes": [
    { "userId": "uuid1", "code": "xyz9999", "shortUrl": "..." },
    ...
  ]
}
```

Steps: look up `Link`, verify it is `Mode == UserAttributed`, call `ILinkService.CreateUserLinkCodesAsync`, return results.

Idempotency: if a `(linkId, userId)` code already exists, return the existing code rather than an error.

---

### `GET /links/{linkId}/codes/{userId}`
Return the code for a specific user + their visit history.

---

### `DELETE /links/{linkId}/codes/{userId}`
Deactivate a user's code. Invalidate cache.

---

## Step 4: Analytics endpoints

### `GET /links/{id}/analytics`
Return aggregate stats for a Mode 1 link.

Query params: `from`, `to` (ISO 8601 dates), `groupBy` (`day` | `hour`).

Response:
```json
{
  "totalClicks": 1234,
  "uniqueIps": 987,
  "byPeriod": [ { "period": "2025-01-01", "clicks": 42 } ],
  "topReferrers": [ { "referrer": "https://twitter.com", "clicks": 80 } ]
}
```

---

### `GET /links/{linkId}/analytics/users`
Return per-user click totals for a Mode 2 link. Supports pagination.

---

## Step 5: API key management endpoints

### `POST /api-keys`
Create a new API key. Returns plaintext key once — never again.

### `GET /api-keys`
List all keys (prefix, name, scopes, expiry — never the hash).

### `DELETE /api-keys/{id}`
Revoke a key (sets `IsActive = false`).

---

## Step 6: Rate limiting on creation endpoints

Separate rate limit policy for creation endpoints (lower than redirect):
- `POST /links`: 100 requests / 10 minutes per API key
- `POST /links/{linkId}/codes`: 10 requests / minute per API key, max 10,000 user IDs per request

---

## Verification

1. `POST /links` without `X-Api-Key` → `401`
2. `POST /links` with valid key → `201`, code in DB
3. `GET /{code}` for new link → `302` (redirect pipeline picks up new code)
4. `PATCH /links/{id}` with `{ "isActive": false }` → subsequent redirect returns `404`
5. `POST /links/{linkId}/codes` with 1,000 userIds → all codes in DB, response lists all
6. Call `POST /links/{linkId}/codes` twice with same userId → same code returned (idempotent)

Next: [Phase 6 — Admin UI](06-admin-ui.md)
