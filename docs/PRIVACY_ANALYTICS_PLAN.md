# Privacy-Respecting Visit Analytics — Feature Plan (reconciled with Phase 0)

Extract genuinely useful analytics from redirect requests **without building a fingerprint of the
end-user**. The guiding principle already exists in the codebase: `HashIp` (`BackgroundVisitWriter`)
keeps only the analytic value of the IP under an HMAC (secret pepper + hourly-rotating component) and
the raw IP never reaches the database. This plan extends that **"derive at the edge, discard the raw"**
discipline to every other request signal.

> **Status:** this is **Phase 0.5** — it hardens the exact pipeline Phase 0 built. Phase 0 already
> derives *some* dimensions but still persists the raw signals alongside them; this plan closes that
> gap. It is independent of the social work and should land **before Social Phase 1** (which only adds
> more data surface). See [SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md).

## Where Phase 0 already got us (do not rebuild)
Phase 0 (shipped) introduced derive-at-ingest for two signals, in `BackgroundVisitWriter.FlushAsync`
via the pure `SourceDetector`:
- ✅ **`ClickSource Source`** — platform bucket from the referrer host (Twitter/Bluesky/Reddit/…/Other).
- ✅ **`DeviceType Device`** — coarse device class (Desktop/Mobile/Tablet/Bot) from the User-Agent.
- ✅ The pattern this plan wants (small pure reducers, run once at write time, stored as low-cardinality
  columns) is established and unit-tested.

## The gap Phase 0 left (the motivation) — we now store raw **and** derived
`VisitEntity` / `UserVisitEntity` today carry `HashedIp`, `ClickedAt`, **`Source`**, **`Device`** —
**and still persist raw `Referrer` (full URL)** and **raw `UserAgent`**. So we hash the IP diligently
and then keep the two fields that most undercut it:
- **Raw User-Agent is a fingerprint.** With the hashed IP + referrer inside one hourly window it can
  re-identify an individual. We already derive `Device` from it — we should also take
  *browser / OS / bot* and then **stop storing the raw string**.
- **Full Referrer leaks private context.** Path + query of the referring URL can carry search terms,
  session tokens, internal page structure. We only need the registrable **host** (and we already
  bucket the platform into `Source`).

## Signal treatment plan
Everything is derived in `FlushAsync`, right where the IP is already hashed and `Source`/`Device`
already come from `SourceDetector`. Store the low-entropy derived value; discard the raw.

| Signal | Treatment | Phase 0 status |
|---|---|---|
| IP | ✅ Keep HMAC hash. Add **GeoIP → country** at ingest; store country, discard raw. | hash shipped; geo new |
| User-Agent | Keep `Device`; **also** derive `{ Browser, Os, IsBot }`; **stop storing the raw string**. | `Device` shipped; browser/OS + raw-drop new |
| Referer | Keep `Source`; **also** store registrable **host** (`ReferrerHost`); **drop the full URL**. | `Source` shipped; host + raw-drop new |
| Accept-Language | Store **primary language** tag (`en`); drop the q-weighted list. | new |
| Sec-Fetch-* | Derive a **navigation type** (real click vs prefetch/embedded). | new |
| DNT / Sec-GPC | If `1`, fall back to **aggregate-only** — leave the derived dimension columns null. | new |

## Deliberate non-goals (the exploitative path we reject)
- **No high-entropy Client Hints** — never send `Accept-CH` for model/arch/full-version.
- **No JS interstitial** to harvest screen size / timezone / canvas. A 302 is inherently
  low-surveillance; keep it that way.
- **No tracking cookie** set on the redirect.
- **No city-level geo + ISP/ASN** retention (a re-identifying combination).

## Data model changes (reconciled with Phase 0's columns)

### `VisitEvent` (capture-time record) — extend the redirect handler
`MapGet("/{code}")` already reads `Referer` and `User-Agent`. Add `Accept-Language` and `Sec-Fetch-*`,
and pass raw values through unchanged — **all reduction stays in `FlushAsync`**, off the hot path.

```csharp
public sealed record VisitEvent(
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId,
    string RawIp,
    string? Referrer,       // raw; reduced to host in FlushAsync, not persisted
    string? UserAgent,      // raw; parsed to buckets in FlushAsync, not persisted
    string? AcceptLanguage, // new
    string? SecFetchSite,   // new
    bool PrivacySignal,     // new: DNT:1 or Sec-GPC:1
    DateTimeOffset ClickedAt);
```

### Entities — keep Phase 0's enums, add derived columns, drop the raw ones
`VisitEntity` and `UserVisitEntity` change the same way. **Keep** the Phase 0 fields (`Source`,
`Device`, `HashedIp`, `ClickedAt`); **remove** `Referrer` and `UserAgent`; **add**:

```csharp
// keep: ClickSource Source;  DeviceType Device;  (Phase 0)
// remove: string? Referrer;  string? UserAgent;
public string? Browser { get; set; }        // e.g. "Chrome"
public string? Os { get; set; }             // e.g. "Windows"
public string? ReferrerHost { get; set; }   // registrable host of the referrer (was the full URL)
public string? Country { get; set; }        // ISO-3166 alpha-2, or null
public string? Language { get; set; }       // primary subtag, e.g. "en"
public string? NavigationType { get; set; } // from Sec-Fetch-Site
```
When `PrivacySignal` is set, the derived dimension columns are left `null` (the row still counts the
click, carries no profile). `IsBot` is already represented by `Device == DeviceType.Bot`, so no
separate column — the UA parser just feeds `Device`.

### Migration — a follow-up on top of Phase 0, not a rewrite
Phase 0's `AddVisitSourceAttribution` (Source/Device columns) is shipping to prod in PR #11. This adds
a **new** migration in the PostgreSQL provider (and SQLite dev if present): drop `UserAgent` /
`Referrer`, add the columns above. No back-fill — historical rows get `null` dimensions and lose the
raw fields (acceptable and on-posture).

## Enrichment services (`ShortLynx.Services/Analytics`)
Small, pure, unit-testable, alongside the existing `SourceDetector`; injected into
`BackgroundVisitWriter`.

- **`IUserAgentParser`** → `(Browser, Os, DeviceType, IsBot)`. Fold the existing
  `SourceDetector.DetectDevice` logic into it (so device classification lives in one place) and extend
  with browser/OS. Dependency-free heuristic table first; interface lets us swap in UAParser later.
- **`IReferrerReducer`** → registrable host from a raw referrer (`Uri` parse, strip `www.`). Pairs with
  the existing `SourceDetector.DetectSource` (which stays for the platform enum).
- **`ILanguageReducer`** → primary subtag from `Accept-Language`.
- **`IGeoIpResolver`** → country from IP. **Adds a dependency** (see open decision); ships behind the
  interface with a **no-op default returning `null`** so the rest lands without it.

## Impact on already-shipped surfaces (must update in the same change)
- **Admin clicks table** (`ClicksTable` / `ClickRow`) currently shows the **full referrer** and filters
  on it. Switch its column + filter to `ReferrerHost`; the "referrer contains" filter still works, now
  over the host. Drop the dependency on the raw `Referrer` column.
- **`/me/links/{id}/analytics`** and the recent-clicks projections read `Referrer`/`UserAgent`; repoint
  to the derived columns. `Source`/`Device` breakdowns are unaffected.

## Structural improvement — aggregate Mode 1 (follow-up, not this phase)
Anonymous (Mode 1) links exist for aggregate tracking yet still write one row per click. Follow-up:
collapse Mode 1 to **aggregate counters** (clicks per code per hour + dimension breakdowns), removing
the per-row correlation surface for the mode that never needed identity. Mode 2 (user-attributed)
legitimately keeps per-visit rows — consented, tied to a recipient code. Tracked separately.

## Retention hook
Unblocks the still-open retention policy: keep per-visit detail briefly, then roll up into aggregates
and drop rows. The derived columns above are the rollup dimensions. Out of scope here beyond noting it.

## Open decision — GeoIP provider
- **MaxMind GeoLite2** (local `.mmdb`, no per-request network, needs license key + DB in the image), or
- a **lookup API** (no bundled DB, adds latency + a third party sees IPs — weaker posture).

Recommendation: **GeoLite2 local DB** (keeps IPs in-process). Ships last; everything else is independent.

## Tests
- Parsers: UA → `{Browser,Os,Device,IsBot}` incl. bot detection; referrer → host (`null` on garbage);
  `Accept-Language` → primary subtag; privacy signal → dimensions null.
- `BackgroundVisitWriter`: raw UA/referrer in `VisitEvent` **never persisted**; derived columns
  populated; `Source`/`Device` still correct (regression on Phase 0); `PrivacySignal` row persists with
  null dimensions but valid `HashedIp`/`ClickedAt`.
- Redirect handler: new headers flow into `VisitEvent`; absent headers → `null`, no throw.
- Admin: `ClicksTable` renders/filter on host; no raw referrer leaks into the DOM.

## Commit breakdown
1. `IUserAgentParser` (absorbs `DetectDevice`) + `IReferrerReducer` + `ILanguageReducer` + tests — no new deps.
2. `VisitEvent` + redirect handler new headers + `FlushAsync` reduction wiring + writer tests.
3. Entity columns + EF migration (drop raw `UserAgent`/`Referrer`, add derived); update analytics reads.
4. Admin `ClicksTable`/`ClickRow` → `ReferrerHost`; verify no raw-referrer display.
5. `IGeoIpResolver` (no-op default) wired into `FlushAsync`; GeoLite2 impl behind the open decision.
6. Docs (DESIGN.md privacy posture + non-goals; redirect-pipeline enrichment step; open-decisions).

## Out of scope (this phase)
- Mode 1 aggregate-counter rewrite (follow-up plan).
- Retention/rollup job implementation.
- Client Hints, JS interstitial, any cookie-based identity — permanently rejected, not deferred.
