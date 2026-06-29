# Phase 1 — Resolve Open Design Decisions

These questions must be answered before any code is written. Record the decision next to each item and update the relevant phase file before starting it.

---

## Schema & Entities

**Q1: Table layout for links**

Options:
- A) `links` table + `user_link_codes` table — clean separation, no discriminator, clear foreign keys
- B) Single `links` table with nullable `user_id` discriminator column

Recommendation: Option A. The two modes have structurally different data (anonymous links have no user context; user-attributed codes carry both `link_id` and `user_id`). A shared table creates nullable columns throughout and complicates indexes.

Decision: A

---

**Q2: Visits table layout**

Options:
- A) Separate `visits` (Mode 1 aggregate) and `user_visits` (Mode 2 attributed) tables
- B) Single `visits` table with a nullable `user_link_code_id` FK

Recommendation: Option A. The write path and query path differ significantly — Mode 1 writes aggregate records, Mode 2 writes attributed records. Separate tables allow provider-specific optimizations (e.g., PostgreSQL `UNLOGGED TABLE` only on `visits`).

Decision: A

---

**Q3: Denormalize `user_id` onto `user_visits`?**

If using separate tables (Q2 Option A), store `user_id` directly on `user_visit` rows to avoid a join when querying "all visits for user X".

Recommendation: Yes. The `user_id` is immutable for a given `user_link_code`, so denormalization is safe and the query benefit is real.

Decision: Yes

---

## Short Code Specifics

**Q4: Default code length**

- 7 chars of Base62 = ~3.5 trillion combinations — ample for most deployments
- 8 chars = ~218 trillion

Recommendation: 8 characters. Makes collision probability negligible even without a Bloom filter.

Decision: 8

---

**Q5: Is code length configurable per deployment?**

Options:
- A) Fixed constant in code
- B) `appsettings.json` key `ShortCode:Length`, validated on startup

Recommendation: Option B. Minimal cost to implement, removes a fork incentive for operators who want longer codes.

Decision: B

---

**Q6: Hash-based code algorithm (Mode 2)**

- Hash function: SHA-256
- Input: `linkId.ToString() + "|" + userId.ToString()` (pipe separator avoids accidental collisions)
- Take first N bytes, Base62-encode, truncate to configured length
- Base62 alphabet: `0-9A-Za-z` (standard, no ambiguous chars)
- Collision handling: retry with `$"{linkId}|{userId}|{attempt}"` (attempt starts at 1)

Decision: Yes

---

## Operational

**Q7: Minimum supported .NET version**

- .NET 10 only: simpler, uses `Guid.CreateVersion7()` natively
- .NET 8+: requires `NewId` / `UlidSharp` fallback

Recommendation: Target .NET 10 only. The project is greenfield and .NET 8 LTS support ends mid-2026. Revisit for .NET 8 compatibility as a follow-up if there is demand.

Decision: .NET 10

---

**Q8: Redis — required dependency or abstracted?**

Options:
- A) `IDistributedCache` (Microsoft abstraction) — no extra interface needed; works with Redis, in-memory, SQL Server backed caches
- B) Custom `ICacheProvider` interface
- C) Redis hard-wired (simpler, explicit)

Recommendation: Option A. `IDistributedCache` is well-understood, avoids a custom interface, and lets operators use in-memory cache for SQLite/dev deployments without Redis.

Decision: A

---

**Q9: Data retention defaults for visit events**

- 90 days, configurable via `appsettings.json` key `Retention:VisitEventDays: 90`
- Enforced by a background cleanup job (see Phase 8)

Decision: Yes

---

**Q10: Interstitial "leaving this site" warning page**

Opt-in via `appsettings.json` key `Redirect:Interstitial: false`. If enabled, redirect serves an HTML page instead of a 302 header.

Decision: Yes

---

## Mode 2 Specifics

**Q11: One-time-use vs. multi-use for user-attributed codes**

Recommendation: Multi-use by default, with an opt-in `IsOneTimeUse` flag on the `user_link_code` entity. One-time-use codes set `IsUsed = true` after first redemption and return `410 Gone` on subsequent attempts.

Decision: Yes

---

**Q12: Status code for expired/one-time-used codes**

- `404` Not Found — hides the existence of the link
- `410 Gone` — semantically correct, may leak info

Recommendation: `410 Gone` only for explicitly one-time-use codes after redemption. Return `404` for expired or deactivated codes to maintain consistent enumeration resistance.

Decision: Yes

---

**Q13: Click deduplication**

Recommendation: Deduplicate within a 1-hour window per `(code, hashed_ip)` pair using a Redis Set / sliding expiry. Configuration key: `Redirect:DeduplicationWindowMinutes: 60`. Default off — simple self-hosters may not want Redis for this.

Decision: yes, but 30 minutes instead.

---

## API & Authentication

**Q14: Authentication model**

Options:
- A) API key only (simpler, suitable for server-to-server use)
- B) API key + OAuth2 / OIDC (supports human admin users)

Recommendation: Phase 1 — API key only for creation/management endpoints. Phase 2 — add OAuth2 support behind a feature flag.

Decision: Yes, eventually I want an admin portal as an option, but not required.

---

**Q15: Link owner concept**

Is there a concept of a "link owner" separate from the attributed user?

Recommendation: Yes. An API key is the logical "owner" / tenant. A link belongs to the API key that created it. `user_link_code` rows belong to that link, and therefore to the same API key. This supports multi-tenant self-hosting without a full OAuth flow.

Decision: Yes, I want that to be a feature, but this is also intended specifically for self-hosted solutions and attaching to a single domain of the same host.

---

## Once All Decisions Are Made

Update the relevant phase files, then proceed to [Phase 2 — Data Layer](02-data-layer.md).
