# Plan: Operable Admin Portal

## Goal
Turn the read-only dashboard into a working portal: **create + revoke API keys** and
**create + manage links**, all tenant-scoped. Mode 2 (per-recipient user codes) is an optional
follow-on.

---

## Decision #1 — Link ownership model — ✅ DECIDED: Option C

> **Decided:** dashboard links are owned directly by a user. Implement **Option C**. Options A and B
> are retained below for rationale/record only.

`LinkEntity.ApiKeyId` is **required** today, and dashboard tenant scoping flows through
`Link.ApiKey.UserAccountId == currentUser`. So every link is owned by an API key, and a user owns
links transitively through their keys. When a user creates a link from the dashboard, something has
to own it. Three options:

### Option A — User picks one of their API keys
The create-link form shows a dropdown of the user's keys; the link is created under the selected one.
- **Pros:** no schema change; explicit.
- **Cons:** awkward UX (why choose a key to make a link?); a brand-new user has **zero keys**, so
  they can't create a link until they first create a key. Conceptually muddled — API keys are for
  programmatic access, not "folders" for manual links.

### Option B — Auto-provision a hidden per-user "dashboard" key
On first link creation, get-or-create a special key owned by the user and create dashboard links
under it automatically. The user never sees or thinks about it.
- **Pros:** no schema change; clean UX (links "just work").
- **Cons:** a phantom key per user exists only to satisfy the FK; it'd appear in the keys list unless
  filtered/hidden; slightly muddies "keys = integrations."

### Option C — Add nullable `LinkEntity.UserAccountId` ✅ (chosen)
A link is owned by **either** an API key (programmatic) **or** a user directly (dashboard-created).
- Make `LinkEntity.ApiKeyId` nullable; add `UserAccountId Guid?`.
- Scoping becomes `Link.UserAccountId == user OR Link.ApiKey.UserAccountId == user`.
- **Pros:** conceptually correct (keys = programmatic, manual links = user); no phantom keys.
- **Cons:** schema change (migration); `ApiKeyId` becomes nullable; update the four dashboard scoping
  queries; add a `LinkService` overload `CreateAnonymousLinkAsync(url, UserAccountEntity owner)`. The
  redirect/analytics paths are unaffected (they go ShortCode → Link, owner-agnostic).

**Decision: Option C.** It's the honest model (keys = programmatic, hand-made links = the user), a small
contained data change, and it avoids every edge case A and B carry (no "create a key before your first
link," no phantom rows). A and B are recorded above only as the considered alternatives.

### Other decisions
2. **Test depth** — service tests only, or also **bUnit** component tests (adds the `bunit` package +
   an Admin project reference)? Recommend both.
3. **Mode 2 now** or keys+links first? Recommend keys+links first.

---

## Changes by area

### A. ShortLynx.Services *(new code)*
- `ApiKeyService.RevokeAsync(Guid keyId, Guid userAccountId, ct)` (+ interface) → set `IsActive=false`,
  scoped to owner; no-op/false if not owned or unknown.
- *(If link management includes deactivate)* `DeactivateLinkAsync(linkId, userAccountId)`.
- `CreateAnonymousLinkAsync(url, UserAccountEntity owner, ct)` overload that sets `UserAccountId` and
  leaves `ApiKeyId` null (the dashboard creates user-owned links).

### B. ShortLynx.Admin DI *(registration only — services already exist in ShortLynx.Services)*
Add to `AddShortLynxServices`:
- `ILinkService → LinkService`
- `IShortCodeGenerator → HashBase62Generator`
- `IUrlValidationService → UrlValidationService`
- `Configure<ShortCodeOptions>`, `Configure<UrlValidationOptions>`

### C. Blazor write pattern *(important)*
Read pages use `IDbContextFactory` to avoid a long-lived scoped `DbContext`. The write services take a
**scoped** `DbContext`, and in Blazor Server the scope is the whole circuit — the classic pitfall. Run
each write in a fresh DI scope:
```csharp
@inject IServiceScopeFactory ScopeFactory
await using var scope = ScopeFactory.CreateAsyncScope();
var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
var (record, plaintext) = await svc.CreateAsync(name, scopes, CurrentUserId);
```
(Mirrors `BackgroundVisitWriter`.)

### D. Admin UI components
- **ApiKeys.razor** — "Create key" `EditForm` (name + scope checkboxes) → `CreateAsync` → show the
  plaintext key **once** with copy + "you won't see this again." Per-row **Revoke** (confirm) →
  `RevokeAsync`, then refresh.
- **Links.razor** — "Create link" `EditForm` (just a URL — links are user-owned) →
  `CreateAnonymousLinkAsync(url, currentUser)` → show the short code with copy. Surface URL-validation errors
  (format/SSRF). Optional per-row **Deactivate**.
- **LinkDetail.razor** — optional deactivate; if Mode 2 is in scope, a "provision user codes" form.
- Small **copy-to-clipboard** JS interop (`navigator.clipboard`) — fits the TypeScript path we set up.
- DataAnnotations validation (`[Required]`, `[Url]`, "≥1 scope").

### E. Authorization
Every create/revoke/deactivate is scoped to the current user (`NameIdentifier` claim): keys created
with `userAccountId = current`; revoke/deactivate verify ownership. These flows operate on the user's
**own** data even for super-admins (cross-tenant admin is a separate feature).

---

## Other projects/services impacted

| Project | Change |
|---|---|
| **ShortLynx.Services** | New `ApiKeyService.RevokeAsync` (+ interface); optional link-deactivate; `CreateAnonymousLinkAsync(url, user)` overload |
| **ShortLynx.Admin** | Register `ILinkService` + `IShortCodeGenerator` + `IUrlValidationService` + options; new create/revoke UI; scope-per-write pattern |
| **ShortLynx.Data** | Nullable `LinkEntity.UserAccountId` + nullable `ApiKeyId` + migration; update the four dashboard scoping queries to `UserAccountId == user OR ApiKey.UserAccountId == user` |
| **ShortLynx.Core** | No change needed (already exposes these via the API); shared services keep behavior consistent |
| **ShortLynx.Tests** | New service tests; optionally `bunit` + Admin project reference for component tests |

---

## Tests

### Service tests *(xUnit, existing pattern under `ShortLynx.Tests/Services/`)*
- **RevokeAsync**: revokes own active key (`IsActive=false`); no-op for another user's key; unknown id;
  idempotent on already-revoked.
- **CreateAsync ownership**: created key has `UserAccountId = current`; appears in that user's scoped
  query, not another's.
- **CreateAnonymousLinkAsync** under a user's key (or user, Option C): correct owner; short code
  minted; URL validation rejects malformed/SSRF URLs (reuse `StubUrlValidationService` or the real one).
- **Tenant isolation** (extend `TenantScopingTests`): a user's revoke/deactivate never touches another's
  rows; Option C scoping returns both user-owned and key-owned links for the owner only.
- *(If deactivate)* a deactivated link/short code no longer resolves — `RedirectService` already filters
  `IsActive`, so assert lookup returns null.

### Component tests *(bUnit — new infra)*
- Add `bunit` package + `<ProjectReference>` to ShortLynx.Admin in the test project.
- Use bUnit `AddTestAuthorization()` to simulate the signed-in user (claims) and the `SuperAdmin` policy.
- **ApiKeys create form**: valid submit → renders one-time plaintext panel; missing name / no scope →
  validation messages; revoke confirm → calls the faked service with the right id.
- **Links create form**: valid URL → renders short code; invalid URL → error surfaced.
- Inject fake `IApiKeyService`/`ILinkService`; assert the component passes `currentUserId` and renders
  results — cheaply verifies one-time-key display, validation, and ownership wiring (no DB/HTTP).

### Skip for now
A full Admin `WebApplicationFactory` E2E harness (cookie auth + Blazor circuits) is high-cost; bUnit
covers component logic far more cheaply. Add E2E only if you want browser-level coverage later.

---

## Verification
- `dotnet test` green (178 existing + new).
- Manual: log in → create key (plaintext shown once) → create link → hit `Web /{code}` → 302 + visit
  recorded → revoke key (Core API auth fails) → deactivate link (redirect 404s).
- Tenant isolation: a second user can't see or revoke the first's keys/links.

## Suggested sequence
Data migration (nullable `UserAccountId`/`ApiKeyId` + scoping `OR`) → `RevokeAsync` + tests →
Admin DI registration → ApiKeys create/revoke UI + bUnit → Links create UI + bUnit →
(optional) deactivate / Mode 2 → verify.
