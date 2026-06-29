# ShortLynx — Design Document

_Condensed from initial architecture discussion. Captures decisions made and open questions remaining._

---

## Overview

ShortLynx is an open-source .NET + EF Core short-link application with two distinct link modes: anonymous aggregate tracking and per-user click attribution. It is designed to be self-hosted with PostgreSQL as the default database, but adaptable to other providers.

---

## Decided: Application Modes

### Mode 1 — Anonymous Short Links
- A destination URL is shortened to a single short code
- Clicks are tracked in aggregate (count, timestamp, referrer, hashed IP)
- No user identity is involved at any point in the pipeline
- Short code generated from the link ID alone

### Mode 2 — User-Attributed Short Links
- A unique short code is minted **per user, per link**
- The link has no owner — the user is the **clicker**, not the creator
- When a user's personalized code is clicked, that visit is attributed to them
- No authentication is required at click time — the code itself carries the identity
- Codes are created in **bulk, ahead of time** (Option A — batch generation)
- This model is suited for email campaigns, sales tracking, and personalized onboarding flows

---

## Decided: Short Code Generation

- **GUID IDs break the bit-pack approach** — two 128-bit GUIDs encode to ~43 Base62 characters, which is not a short link
- **Code generation is decoupled from entity IDs entirely**
- A pluggable `IShortCodeGenerator` interface will be defined, with multiple built-in strategies:

| Strategy | Notes |
|---|---|
| **Hash-based deterministic** (recommended for Mode 2) | `Base62(truncate(SHA256(linkId + userId)))` — same inputs always produce the same candidate code, supporting natural idempotency |
| **Random Base62** (recommended for Mode 1) | Random N-character string, uniqueness enforced by DB constraint |
| **Sequential counter + Base62** | Requires a counter (DB sequence or Redis INCR); shortest codes, zero collisions, but adds infrastructure dependency |

- Idempotency for Mode 2 is enforced at the **database layer** via a `UNIQUE (link_id, user_id)` constraint — not in the code generation strategy itself
- Collision handling: retry with a salt appended; the `UNIQUE (code)` constraint is the safety net

---

## Decided: Entity IDs

- Both link and user entities use **GUID primary keys**
- **Sequential GUIDs** are generated at the **application layer** using `Guid.CreateVersion7()` (.NET 9+)
  - Provider-agnostic — does not rely on any DB-specific function
  - Time-ordered, safe for clustered indexes on all providers
  - EF Core configured with `ValueGeneratedNever()` on all GUID PKs
- For .NET 8 compatibility, a fallback library (`NewId` or `UlidSharp`) will be needed — minimum .NET version is an open question

---

## Decided: Redirect Pipeline

```
GET /{code}
    │
    ├─ Rate limit check (by IP) ──── exceeded → 429
    │
    ├─ Cache lookup by code ──────── miss → DB lookup → populate cache
    │       │
    │       └─ not found / inactive / expired → 404
    │
    ├─ Return 302 immediately ◄──── user is redirected, browser navigating
    │
    └─ Enqueue VisitEvent (non-blocking)
            │
            └─ BackgroundService
                    ├─ Hash IP
                    ├─ Parse referrer / user agent
                    └─ Batch write to visits table
```

- Cache key is always the **short code string** — never the GUID
- Cached value: `{ OriginalUrl, LinkId, UserId, IsActive, ExpiresAt }`
- Cache must be **invalidated immediately** on any link mutation (deactivation, deletion, expiry)
- The redirect and the visit recording are fully decoupled — user experiences only the cache lookup and the 302

### Background Visit Writer
- Default implementation: in-memory `Channel<T>` drained by a `BackgroundService`
- Visits are batched before writing to the DB for throughput efficiency
- **Known tradeoff**: in-process channel means visit events in-flight at crash time are lost. Acceptable for most self-hosted use cases.
- A pluggable `IVisitEventSink` interface will allow contributors to swap in persistent queue implementations (Hangfire, RabbitMQ, Azure Service Bus, etc.)

---

## Decided: Database & Provider Strategy

- **Default provider: PostgreSQL**
- **Supported at launch: PostgreSQL, SQLite** (SQLite for local dev and lightweight self-hosting)
- **Planned via community contribution: SQL Server, MySQL/MariaDB**

### Provider Registration
- Single configuration key selects the provider at the composition root:
```json
{
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "..."
  }
}
```
- Provider wiring is isolated to one `AddShortLynxDatabase()` extension method in `Program.cs`
- The rest of the application is provider-agnostic

### Migrations
- **Separate migration project per provider** (e.g., `ShortLynx.Data.PostgreSQL`, `ShortLynx.Data.Sqlite`)
- All projects reference a shared `ShortLynx.Data` project containing entities and `DbContext`
- Each provider migration assembly is specified via `MigrationsAssembly(...)` in the provider registration

### Bulk Operations
- A `IDbOperations` interface abstracts provider-specific bulk insert and upsert logic
- **Default implementation**: `EFCore.BulkExtensions` (supports PostgreSQL, SQL Server, SQLite, MySQL)
- **PostgreSQL override**: native binary import or `ON CONFLICT DO NOTHING` for maximum throughput
- Used by: batch user code creation (Mode 2), background visit writer

### PostgreSQL-Specific Optimizations (documented as PostgreSQL-only)
- `UNLOGGED TABLE` for the visits table — skips WAL, faster write throughput, acceptable durability tradeoff for analytics data
- Native `uuid` type — more compact than `CHAR(36)`, efficient indexing
- `ON CONFLICT DO NOTHING` — cheap idempotent bulk upserts
- Partial indexes — e.g., index only active links for the redirect lookup

---

## Decided: Security Considerations

| Risk | Mitigation |
|---|---|
| Short code enumeration | Rate limit redirect endpoint by IP; consistent response times for invalid vs. inactive codes |
| User-attributed link forwarding | Document the attribution model's limits; optionally support short TTLs or one-time-use codes |
| Open redirect / phishing | Validate destination URLs against Google Safe Browsing or equivalent on creation; maintain domain blocklist |
| SSRF | Validate submitted URLs against private IP range blocklist before any server-side fetch; no redirect following in validation HTTP client |
| Bulk creation abuse | Authenticate creation endpoints with API keys; enforce per-key rate limits and quotas |
| API key storage | Store HMAC-SHA256 or Argon2 hash only; store key prefix (`first 8 chars`) in cleartext for lookup; issue plaintext key once at creation only |
| Click data privacy (GDPR/CCPA) | Hash IP addresses with a rotating salt; design in data retention policy and deletion endpoints from the start |
| Cache integrity | Redis must require authentication and must not be publicly exposed |

---

## Still To Be Decided

### Schema & Entities
- [ ] Full EF Core entity model — properties, relationships, indexes, constraints
- [ ] Whether `links`, `short_links`, and `user_link_codes` are fully separate tables or share a discriminator column
- [ ] `visits` and `user_visits` as separate tables vs. a single table with nullable user FK
- [ ] Whether to denormalize `user_id` onto `user_visits` for query performance (likely yes, worth confirming)

### Short Code Specifics
- [ ] Default code length (7–8 chars of Base62 is the typical recommendation)
- [ ] Whether code length is configurable per deployment
- [ ] Exact bit layout if hash-based: which hash function, how many bytes taken, Base62 alphabet definition

### Operational
- [ ] Minimum supported .NET version (affects `Guid.CreateVersion7()` availability)
- [ ] Whether Redis is a required dependency or abstracted behind an `ICacheProvider` interface
- [ ] Data retention policy defaults for visit events
- [ ] Whether an interstitial "leaving this site" warning page is supported (opt-in config)

### Mode 2 Specifics
- [ ] One-time-use vs. multi-use for user-attributed codes — currently multi-use assumed
- [ ] Whether expired or single-use codes return `404` or `410 Gone`
- [ ] Click deduplication strategy — should rapid repeated clicks on the same code within a time window count as one visit?

### API & Authentication
- [ ] Full API surface design — endpoint shapes for link creation, user code batch generation, analytics queries
- [ ] Authentication model for creation endpoints: API key only, or OAuth2 as well
- [ ] Whether there is a concept of a "link owner" / admin separate from the attributed user identity

### Future / Optional Features
- [ ] Custom domain support
- [ ] Vanity / custom slug support (namespace collision strategy with auto-generated codes)
- [ ] Analytics query API — what aggregations and filters are exposed
- [ ] Bloom filter as pre-flight collision check for high-volume deployments
