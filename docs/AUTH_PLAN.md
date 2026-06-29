# Plan: Framework-Agnostic Auth API (bring-your-own-frontend)

## Goal
Make `ShortLynx.Core` a **complete backend** that any frontend framework (Next.js, SvelteKit, native,
…) can authenticate users against and drive. The Blazor `Admin`/`Web` apps remain the **low-config
default** frontend; this work makes them *one* option rather than the only one.

## Why this is needed (the gap)
Today Core authenticates **only** with API keys (`ApiKeyAuthHandler`, `Bearer <key>`). Interactive
user auth (magic-link → session) lives entirely inside the Blazor Admin app (its `Confirm` page
validates the token and issues a cookie). So an external frontend has no way to: log a user in, hold a
session, or read/write that user's own data over HTTP. What already exists and is reusable:
- `POST /magic-links` — sends the magic-link email (anonymous, rate-limited).
- `IMagicLinkService.ValidateTokenAsync(token)` — validates a single-use token and returns the `UserAccount`.
- `Microsoft.IdentityModel.JsonWebTokens` is already referenced in Core.
- `CustomDomainService`, `LinkService`, `ApiKeyService` — all user-scopeable.

## Locked decisions
- **Session credential: BOTH cookie + JWT.** One login dual-issues; one bearer scheme reads either transport.
- **API surface: new `/me/*` user-scoped endpoints.** The existing API-key-scoped `/links` etc. are untouched.
- **Access model: allowlist gate** (same as the Blazor default); the allowlist config moves to a shared location both Core and Admin bind.

---

## Architecture

### Credential design
- **Access token** — JWT (HS256, short TTL ~15 min). Claims: `sub` (user id), `email`, `is_admin`.
  Stateless; validated on every request.
- **Refresh token** — opaque random string (long TTL ~30 days), stored **hashed** in the DB so it is
  **revocable** and **rotated** on each use (reuse of an old token revokes the chain).
- **Two transports, one flow:** `POST /auth/session` returns `{ accessToken, expiresIn, user }` in the
  body **and** sets `httpOnly` cookies (`sl_access`, `sl_refresh`, `SameSite` per config).
  - Cross-origin frontend → uses the **body** tokens via `Authorization: Bearer` (cookies aren't sent cross-site).
  - Same-site frontend → relies on the **cookies** (+ CSRF, below).
- **One bearer auth scheme** (`UserSession`) resolves the access token from the `Authorization` header
  **or** the `sl_access` cookie, so `/me/*` controllers don't care which transport was used.

### Access control (shared allowlist)
Move the allowlist out of `ShortLynx.Admin.Options.AdminOptions` into
`ShortLynx.Services/Auth/AccessControlOptions` (`AllowedEmails`, `SuperAdminEmails`, `IsAllowed`,
`IsSuperAdmin`). Both Core (gates `/auth/session`) and Admin (its existing cookie flow) bind it. The
Blazor `Confirm` page keeps working; only the type's home moves. Empty allowlist = fail-closed
(unchanged). Only allowlisted emails can mint a session; super-admins get `is_admin`.

---

## Phases (test-first; each phase its own commit)

### AUTH-0 — JWT options
> **Prereq:** the Accounts arc ([ACCOUNTS_PLAN.md](ACCOUNTS_PLAN.md), ACC-0…ACC-4) lands first. The
> shared `AccessControlOptions` relocation moves into ACC-0, and the sign-in gate is ACC-4
> (allowlist **or** membership) — so AUTH-0 is just the JWT/cookie config below.
- `ShortLynx.Services/Auth/JwtOptions.cs` — `SigningKey` (32+ chars, fail-fast like `HmacSecret`),
  `Issuer`, `Audience`, `AccessTokenMinutes` (15), `RefreshTokenDays` (30), cookie `SameSite`/`Secure`/domain.
- **Tests:** options validation (empty/short key rejected).

### AUTH-1 — Refresh-token entity + migration
- `RefreshTokenEntity` (Id, UserAccountId FK cascade, TokenHash unique, CreatedAt, ExpiresAt, RevokedAt?,
  ReplacedByTokenId?) + DbContext config + index on `TokenHash`. Postgres migration `AddRefreshTokens`.
- **Tests:** constraint/cascade tests (delete user → tokens gone); unique `TokenHash`.

### AUTH-2 — Session service (issue / refresh / revoke)
- `ShortLynx.Services/Auth/IUserSessionService` + `UserSessionService`:
  - `IssueAsync(user)` → signs a JWT + creates a hashed refresh token; returns access + refresh + expiry.
  - `RefreshAsync(refreshToken)` → validates (exists, not expired, not revoked), **rotates** (revoke old,
    issue new), returns a new pair; reuse of a revoked token → revoke the whole chain + fail.
  - `RevokeAsync(refreshToken)` and `RevokeAllForUserAsync(userId)`.
- HS256 signing via `JwtOptions.SigningKey`; refresh tokens hashed (SHA-256) before storage.
- **Tests:** issue→validate JWT claims; refresh rotates and invalidates the old token; revoked token
  rejected; expired refresh rejected; reuse-detection revokes the chain.

### AUTH-3 — Bearer auth scheme + AuthController
- Register a JWT bearer scheme (`UserSession`) that reads header-or-cookie; keep `ApiKey` scheme for
  `/links` etc. Core now has multiple schemes (no global default; per-endpoint `[Authorize(...Scheme)]`).
- `AuthController` (anonymous, rate-limited under the magic-link policy):
  - `POST /auth/magic-link` — request email (delegates to `IMagicLinkService`; alias of `/magic-links`).
  - `POST /auth/session` — body `{ token }`: validate magic-link token → **allowlist gate** → `IssueAsync`
    → set cookies + return `{ accessToken, expiresIn, user }`. (404/401 on bad/expired/used token;
    403 if the email isn't allowlisted.)
  - `POST /auth/refresh` — refresh cookie or body → new pair (+ reset cookies).
  - `POST /auth/logout` — revoke refresh + clear cookies → 204.
  - `GET /auth/me` — current user `{ id, email, isAdmin }` (requires `UserSession`).
- **Tests (integration via `ApiFactory`):** full flow magic-link → `/auth/session` → call a protected
  endpoint → `/auth/refresh` → `/auth/logout`; non-allowlisted email → 403; reused/expired magic token
  → 401; `/auth/me` without a session → 401; cookie transport works without an `Authorization` header.

### AUTH-4 — `/me/*` user-scoped API surface
Session-authenticated, scoped to `UserAccountId` (links owned directly **or** via the user's API keys —
mirrors the Blazor dashboard queries):
- `GET /me` — profile.
- `GET /me/links`, `POST /me/links` (anonymous or user-attributed), `GET /me/links/{id}`,
  `PUT /me/links/{id}/domain`, `POST /me/links/{id}/codes`, `GET /me/links/{id}/analytics`.
- `GET /me/api-keys`, `POST /me/api-keys`, `DELETE /me/api-keys/{id}` (the user provisions their own keys —
  this is how a frontend bootstraps machine credentials).
- `GET/POST /me/domains`, `POST /me/domains/{id}/verify`, `DELETE /me/domains/{id}` (reuse `CustomDomainService`).
- **Tests:** ownership scoping (user A cannot see/modify user B's links/keys/domains → 404); create→list
  round-trips; super-admin still only sees own data here (cross-tenant stays on the Admin app / future `/admin/*`).

### AUTH-5 — CORS + CSRF + hardening
- `Cors:AllowedOrigins` config; `AddCors`/`UseCors` with `AllowCredentials` for cookie mode; preflight for `/me/*` + `/auth/*`.
- **CSRF for cookie sessions:** double-submit token (a non-httpOnly `sl_csrf` cookie + required
  `X-CSRF-Token` header) enforced on unsafe methods **when authenticated via cookie**; bearer-header
  requests are exempt (not auto-sent).
- **Tests:** CORS preflight allows configured origin / rejects others; cookie+unsafe without CSRF header → 403; bearer path unaffected.

### AUTH-6 — Docs + verify
- `docs/API_AUTH.md` — the frontend integration guide: the magic-link → `/auth/session` exchange, where
  `MagicLink:ConfirmationUrlBase` must point (the **frontend's** callback route), token storage guidance
  (memory + refresh cookie), and the cookie-vs-bearer decision for the integrator.
- DEPLOY.md: add `Jwt:SigningKey`, `Cors:AllowedOrigins`, cookie/SameSite settings, the `AddRefreshTokens`
  migration.
- Full suite green; OpenAPI shows the new endpoints.

---

## Files (new unless noted)
- **Services:** `Auth/AccessControlOptions.cs`, `Auth/JwtOptions.cs`, `Auth/IUserSessionService.cs`,
  `Auth/UserSessionService.cs`; `Data/Entities/RefreshTokenEntity.cs`; DbContext config; Postgres migration.
- **Core:** `Auth/UserSessionAuthHandler.cs` (or `AddJwtBearer` config), `Controllers/AuthController.cs`,
  `Controllers/MeController.cs` (+ split per resource), request/response models, CORS/CSRF wiring in `Program.cs`.
- **Admin:** re-point `AdminOptions` usages to the shared `AccessControlOptions` (behavior unchanged).
- **Tests:** session-service unit tests, auth-flow + `/me/*` integration tests, CORS/CSRF tests, refresh-token constraint tests.

## Out of scope (call out / future)
- **Migrating the Blazor Admin to consume Core's `/auth`** — Admin keeps its own cookie sign-in for now
  (low-config). A later phase could make Admin a pure Core client to remove the duplication.
- **OAuth/social login, password auth, MFA** — magic-link stays the only factor.
- **Cross-tenant `/admin/*` API** (super-admin viewing all tenants over HTTP) — separate effort; `/me/*` is self-scoped only.
- **Open self-service registration** — explicitly deferred; allowlist-only for now (a future `OpenRegistration` flag could relax it).

## Risks / decisions to confirm during build
- **Signing strategy:** HS256 (symmetric secret, only Core issues+validates) is the default. RS256 (so other
  services verify without the secret) only if a downstream needs offline verification — likely unnecessary.
- **Allowlist relocation** touches Admin DI; keep the config **section name** (`Admin`) to avoid a breaking
  config change, or introduce `Access` with back-compat. Decide at AUTH-0.
- **Two cookie+token transports** means more docs; `docs/API_AUTH.md` must be clear or integrators will
  mix them.

## Suggested order
**Prereq: the Accounts arc first** (see the combined build order in [ACCOUNTS_PLAN.md](ACCOUNTS_PLAN.md)).
Then AUTH-0 → AUTH-1 → AUTH-2 → AUTH-3 → AUTH-4 → AUTH-5 → AUTH-6. AUTH-3 is the first point a real
frontend can log in; AUTH-4 is the first point it can do useful work.
