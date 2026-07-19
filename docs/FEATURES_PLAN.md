# Plan: Product Features — Mode-2 UI (Track A) + Custom Domains (Track B)

> **Status: BOTH TRACKS SHIPPED** (verified 2026-07-19). The "Context" below describes the
> half-built starting point and is retained as a historical record — it no longer reflects the
> code. Now in place: `LinkService.CreateUserAttributedLinkAsync` (Mode-2 creation), the
> `Recipient` label column on `UserLinkCodeEntity`, the dashboard provision-codes flow, the
> `LinkMode.UserAttributed` spelling fix, `CustomDomainService` with the verification flow, and
> the host-aware redirect in `ShortLynx.Web`. See [MASTER_PLAN.md](MASTER_PLAN.md) §7 for what
> remains (launch, legal, billing).

## Context
The roadmap's security phase is complete. This phase delivers the two product features that are
currently only half-built in the codebase:

- **Mode 2 (user-attributed codes)** — the service (`CreateUserLinkCodesAsync`), the REST API
  (`POST /links/{id}/codes` + analytics), and the `LinkDetail` analytics view already exist, but there
  is **no dashboard path to create a Mode-2 link or provision codes**, `LinkService` only ever creates
  `Mode = Anonymous` links (always minting a ShortCode), nothing ever sets `IsOneTimeUse` (the field and
  the L1 redirect enforcement added in the security phase are unused), and codes carry no recipient label.
- **Custom domains** — `CustomDomainEntity`, DbContext config, the unique `Domain` index, the cascade
  delete, the migration, and constraint tests all exist, but there is **no service, no UI, no
  verification flow**, and the redirect endpoint (`ShortLynx.Web/Program.cs` `MapGet("/{code}")`) is
  **host-agnostic** — it ignores the `Host` header entirely, so custom domains are inert today.

**Decisions locked** (via clarifying questions):
- **Order:** Track A (Mode-2 UI) first, then Track B (custom domains) as a follow-up phase.
- **Recipient model:** add a nullable `Recipient` label column to `UserLinkCodeEntity`.
- **Domain semantics (Track B):** support **both** — a verified domain serves any code globally
  (default), and a link may optionally be *pinned* to one domain via a nullable FK (see Track B).

**Environment facts that shape the work:**
- Tests build the schema with `EnsureCreated` (model-driven), so schema changes need **no test
  migration**. Only the **PostgreSQL** provider has migration files (`ShortLynx.Data.PostgreSql`);
  there are no SQLite migration files. Each schema change therefore needs **one Postgres migration**.
- `LinkMode.UserAtrributed` is misspelled and referenced in exactly 3 places (enum def + 2 tests).
  EF stores the enum as `int` (`HasConversion<int>`), so renaming the member is value-safe.
- There is **no public-base-URL config** anywhere; building a full short URL (`{base}/{code}`) for the
  dashboard needs a new Admin setting.

---

# Track A — Mode-2 (user-attributed) dashboard UI

### A0 — Schema + enum cleanup
- Add `public string? Recipient { get; set; }` to `ShortLynx.Data/Entities/UserLinkCodeEntity.cs`
  (nullable; human-readable label such as an email or campaign tag).
- Rename `LinkMode.UserAtrributed` → `LinkMode.UserAttributed`; update the 2 test references
  (`ShortLynx.Tests/Data/EntityConstraintTests.cs`).
- Add a Postgres migration `AddUserLinkCodeRecipient` (`dotnet ef migrations add … --project
  ShortLynx.Data.PostgreSql`). Confirm the model snapshot updates. No SQLite migration (EnsureCreated).
- **Verify:** `EntityConstraintTests` still green; migration `Up` adds the nullable column only.

### A1 — Service: create Mode-2 links + label/one-time provisioning
`ShortLynx.Services/Links/LinkService.cs` + `ILinkService.cs`:
- New: `Task<LinkEntity> CreateUserAttributedLinkAsync(string url, Guid userAccountId, CancellationToken)`
  — validates the URL, creates `LinkEntity { Mode = UserAttributed, UserAccountId = … }`, **mints no
  ShortCode** (Mode-2 links resolve only via `UserLinkCode`s). Returns the link.
- New richer provisioning overload:
  `Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(Guid linkId,
  IEnumerable<CodeRecipient> recipients, bool isOneTimeUse, CancellationToken)` where
  `record CodeRecipient(Guid UserId, string? Recipient)`. Sets `IsOneTimeUse` and `Recipient` on each
  minted code. Keep the existing `Guid[]` overload delegating to this (Recipient null, one-time false)
  so the Core API is unchanged.
- **Dashboard dedupe:** when provisioning from a pasted label list, skip a recipient if a code with the
  same `(LinkId, Recipient)` already exists (so re-submitting the same list is idempotent even though
  each dashboard recipient gets a fresh `UserId` GUID). Existing `(LinkId, UserId)` idempotency is
  retained for the API path.

### A2 — Admin UI
- `ShortLynx.Admin/Components/Pages/Links.razor` create form: add a **mode toggle**
  (Anonymous / User-attributed). Anonymous → existing `CreateAnonymousLinkAsync`; User-attributed →
  `CreateUserAttributedLinkAsync`, then route to the link's detail page to provision codes.
- `ShortLynx.Admin/Components/Pages/LinkDetail.razor`: for `Mode == UserAttributed`, add a
  **"Provision codes"** panel — a textarea (one recipient label per line), a **one-time-use** checkbox,
  and a submit that calls the service (scoped via `IServiceScopeFactory`, matching `Links.razor`). On
  success, show the minted codes as **recipient → full short URL** with copy + "copy all" (and the
  existing analytics table gains a **Recipient** column).
- Add Admin config `ShortLynx:PublicBaseUrl` (e.g. `https://lynx.example.com`) to render
  `{PublicBaseUrl}/{code}`; bind it through an options type and inject into the components. Document the
  empty-default behavior (render the bare code if unset).

### A3 — Tests
- **Service** (`ShortLynx.Tests/Services/Links/LinkServiceTests.cs`, extend or add):
  - `CreateUserAttributedLink_SetsModeAndOwner_AndMintsNoShortCode`.
  - `CreateUserLinkCodes_StoresRecipientAndOneTimeFlag`.
  - `CreateUserLinkCodes_DedupesByRecipientWithinLink`.
  - existing `Guid[]` overload still mints codes (back-compat).
- **bUnit** (`ShortLynx.Tests/Admin/`):
  - `Links.razor` renders the mode toggle and creating a user-attributed link calls the Mode-2 path.
  - `LinkDetail.razor` provision panel renders for Mode-2, submits a label list, and displays the minted
    codes; the one-time checkbox is honoured (assert `IsOneTimeUse` persisted).
- One-time **redirect** enforcement is already covered by the security-phase L1 tests.

### A4 — Verify
- `dotnet test ShortLynx.slnx` green (197 + new).
- Migration applies cleanly to a Postgres dev DB.
- Manual E2E: create a Mode-2 link → provision 3 labelled codes (one-time on) → hit one code twice
  (second 404s) → confirm attribution + Recipient show in `LinkDetail`.

### A — Files
- **Data:** `Entities/UserLinkCodeEntity.cs`, `Enums/LinkMode.cs`; new Postgres migration + snapshot.
- **Services:** `Links/ILinkService.cs`, `Links/LinkService.cs` (+ `CodeRecipient` record).
- **Admin:** `Components/Pages/Links.razor`, `Components/Pages/LinkDetail.razor`, new options type +
  `appsettings.json` `ShortLynx:PublicBaseUrl`.
- **Tests:** `Services/Links/LinkServiceTests.cs`, `Admin/LinkDetailComponentTests.cs` (new),
  `Admin/LinksComponentTests.cs`, `Data/EntityConstraintTests.cs` (enum rename).

### A — Suggested order
A0 (schema + enum + migration) → A1 (service + tests) → A2 (UI) → A3 (bUnit) → A4 (verify).
Commit as logical chunks: (1) schema/enum/migration, (2) service + service tests, (3) Admin UI +
bUnit, (4) verify.

---

# Track B — Custom domains (outline; detailed when the phase starts)

Sketched now for scope; to be expanded into its own test-first plan before implementation.

- **`CustomDomainService`** (`ShortLynx.Services/Domains/`): `AddAsync(domain, userId)` → normalise,
  generate a DNS-TXT `VerificationToken`, persist `Pending`; `VerifyAsync(domainId)` → look up the TXT
  record via an injectable `IDnsResolver` (real impl wraps `DnsClient`/`System.Net`; tests use a fake),
  compare the token, set `Verified`/`Failed` + `VerifiedAt`; `ListAsync(userId)`, `RemoveAsync`.
- **Admin page `/domains`**: add a domain, show the exact TXT record to create, a "Verify" button,
  status badges, and remove. Super-admin sees all; tenants see their own.
- **Host-aware redirect** (`ShortLynx.Web/Program.cs`): read `ctx.Request.Host`; keep a cached set of
  active verified domains (invalidated on change); the platform's own host is always allowed.
- **Both semantics (locked decision):** default = **global vanity** (any verified host serves any code).
  Add a nullable `CustomDomainId` FK on `LinkEntity` to optionally **pin** a link to one domain; when
  set, the redirect requires `Host == that domain` (else 404). One nullable FK yields both behaviours.
- **Dashboard URL building:** a user's verified default domain feeds the `{base}/{code}` rendering from
  Track A (`PublicBaseUrl` becomes a per-user/per-domain choice).
- **Tests:** service verification with a fake DNS resolver (verified/failed/token-mismatch), host
  allowlist + pinned-domain redirect tests, bUnit domain page.
- **Ops/risks:** new DNS-lookup dependency; TLS/cert provisioning for custom hostnames is a hosting-
  platform concern (document, out of app scope); negative cache already absorbs unknown-host floods.

### Deferred / explicitly out of scope this phase
- TLS certificate automation for custom hostnames (platform/ops responsibility).
- Vanity/custom slug support (separate feature; namespace-collision strategy).
- Analytics query API beyond what `LinkDetail` / Core analytics already expose.
