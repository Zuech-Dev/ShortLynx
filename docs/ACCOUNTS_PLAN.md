# Plan: Accounts, Memberships & Roles (multi-user portals)

## Goal
Let users be **added from the dashboard** with two semantics:
- **Superuser** adds a user → creates a **new account** (independent context) with that user as its Owner.
- **Account Owner/Admin** adds a user → **invites a member into their own account** to co-manage the portal.

This turns today's single-owner model (a `UserAccount` directly owns its links/domains/keys) into
**multi-user accounts (teams)**: an **Account** owns resources; **Users** belong to accounts via
**Memberships** carrying a **role**. This is the tenancy foundation the framework-agnostic auth API
([AUTH_PLAN.md](AUTH_PLAN.md)) layers on top of — so it lands **before** the `/me/*` surface.

## Locked decisions
- **Account/Org entity owns resources** (re-home ownership from `UserAccountId` → `AccountId`).
- **Configurable roles:** `Owner` · `Admin` · `Member` · `Viewer`, with graduated permissions.

## Combined build order (decided — both plans)
Accounts are foundational, so the **Accounts arc lands first**, then the framework-agnostic
[Auth API](AUTH_PLAN.md) layers on top — account-aware from the start, so nothing gets rebuilt:

1. **ACC-0** → **ACC-1** (re-home, the heavy/risky step — front-loaded to de-risk) → **ACC-2** → **ACC-3**
   (dashboard team management ships here — usable value on the existing Blazor app) → **ACC-4** → **ACC-5**.
2. Then the Auth API: **AUTH-0** (now just `JwtOptions` — the allowlist/`AccessControlOptions` move into
   ACC-0, and the sign-in gate is ACC-4) → **AUTH-1** → **AUTH-2** → **AUTH-3** (reuses the ACC-4 gate;
   resolves the account; JWT carries `account_id`+`role`) → **AUTH-4** (account-scoped `/me/*`) →
   **AUTH-5** → **AUTH-6**.

Rationale: ACC-1 rewrites every ownership query, so building any account-scoped surface (dashboard or
`/me/*`) before it would mean redoing it. Accounts-first also ships dashboard "add users" without waiting
on the whole API. A thin "NextJS can log in" slice (AUTH-1/2/3 with user-only claims) is *possible* before
accounts, but its session/claim shape would change once accounts land — so it's not recommended unless a
login prototype is urgently needed ahead of team management.

---

## Data model

### New entities
- **`AccountEntity`** — `Id`, `Name`, `CreatedAt`, `IsActive`. The tenant/workspace that owns resources.
- **`MembershipEntity`** — `Id`, `AccountId` (FK, cascade), `UserAccountId` (FK, cascade), `Role`,
  `CreatedAt`, `InvitedByUserAccountId?`. **Unique** `(AccountId, UserAccountId)`.
- **`AccountRole`** enum — `Viewer < Member < Admin < Owner` (ordered so `role >= Admin` checks work).

### Re-homed ownership (the big change)
Add `AccountId` to **`LinkEntity`**, **`CustomDomainEntity`**, **`ApiKeyEntity`**; scoping becomes a
simple `AccountId == currentAccount` instead of today's `UserAccountId == uid OR ApiKey.UserAccountId == uid`.
- Keep `LinkEntity.ApiKeyId` (provenance: which key created it) and add an optional
  `CreatedByUserAccountId` for audit, but **ownership = AccountId**.
- `ApiKeyEntity` acts on behalf of its `AccountId`; links it creates inherit that account.

### Permissions (single source of truth)
`ShortLynx.Services/Accounts/AccountPermissions` maps role → capability:
| Capability | Viewer | Member | Admin | Owner |
|---|:--:|:--:|:--:|:--:|
| Read resources/analytics | ✅ | ✅ | ✅ | ✅ |
| Create/edit/delete links, domains, API keys | | ✅ | ✅ | ✅ |
| Invite / change-role / remove members (below own role) | | | ✅ | ✅ |
| Rename / deactivate / transfer / delete account | | | | ✅ |

Used by **both** the dashboard (gate UI) and Core (gate endpoints) so rules never drift.

### Sign-in gate (changes the allowlist semantics)
A user may obtain a session if **on the allowlist** (bootstrap owners / platform super-admins) **OR**
**holds ≥1 active membership** (invited members). Brand-new, unlisted, unaffiliated email = no access
(still fail-closed). Super-admin (`IsAdmin`) still comes from `SuperAdminEmails`. This supersedes the
plain allowlist gate in [AUTH_PLAN.md](AUTH_PLAN.md) (AUTH-0/AUTH-3).

---

## Phases (test-first; each phase its own commit)

### ACC-0 — Account/Membership entities + roles + permissions
- Entities above, `AccountRole`, `AccountPermissions`, DbContext config + indexes. No re-home yet.
- **Tests:** permission matrix per role; membership uniqueness; cascade on user/account delete.

### ACC-1 — Re-home resource ownership (+ data migration)
- Add `AccountId` to `Link`/`CustomDomain`/`ApiKey`. Migration `AddAccountsAndMemberships`:
  1. create `Accounts`/`Memberships`;
  2. add nullable `AccountId` to the three resource tables;
  3. **backfill** — one Account per existing owner (`UserAccount`, and one per orphan API key that has no
     user), an Owner `Membership`, and set each resource's `AccountId` from its old owner;
  4. `AlterColumn` `AccountId` → non-nullable.
- Update `LinkService` / `ApiKeyService` / `CustomDomainService` and every scoped query (Core controllers
  + Admin pages) to own/scope by `AccountId`.
- **Tests:** migration backfill leaves every resource with an account; cross-account isolation (account
  A can't see B's resources); existing service/API tests re-pointed to account scoping stay green.

### ACC-2 — Account & membership service
- `ShortLynx.Services/Accounts/IAccountService`:
  - `CreateAccountWithOwnerAsync(name, ownerEmail)` — superuser path (new context).
  - `InviteMemberAsync(accountId, email, role, invitedBy)` — creates the user if new (inactive until
    first login), adds a Membership, sends a magic-link email (reuse `IMagicLinkService`).
  - `ChangeRoleAsync` / `RemoveMemberAsync` / `ListMembersAsync` / `ListAccountsForUserAsync`.
  - All mutating calls enforce `AccountPermissions` against the caller's role (and never let someone act
    above their own role).
- **Tests:** owner invites Member; Admin can't remove an Owner; Member can't invite; role changes;
  superuser creates an account+owner; inviting an existing email reuses the user.

### ACC-3 — Dashboard: account context + member management
- **Account context** — resolve the signed-in user's current account from their membership(s); if they
  belong to several (invited into multiple), an account switcher in the top bar. All existing pages
  (Home/Links/Domains/ApiKeys) scope to the current `AccountId` and **role-gate** write actions.
- **`/members` page** — list members + roles; invite (email + role); change role; remove — each control
  gated by `AccountPermissions` for the current user's role.
- **Superuser** — extend `/users` (or a new `/accounts`) with "Create account + owner" and a cross-account
  list. Super-admins can view any account.
- **Tests (bUnit):** member-management actions render/enabled per role; invite calls the service;
  non-Admin sees no invite control; account switcher swaps scope.

### ACC-4 — Sign-in gate + session context (bridges into auth)
- Update the magic-link **sign-in gate** (Admin `Confirm` + the future Core `/auth/session`) to admit
  allowlisted **or** member users; deny otherwise.
- Carry the **current account + role** into the auth context: Admin cookie claims gain `account_id`/`role`;
  the Core session (AUTH plan) JWT gains `account_id` + `role` claims, with an account selector for
  multi-account users.
- **Tests:** invited (non-allowlisted) member can sign in; unaffiliated email cannot; claims carry account+role.

### ACC-5 — Verify
- Full suite green; migration applies + backfills on a Postgres dev DB; manual E2E: superuser creates an
  account+owner → owner signs in → invites a Member → Member manages links but can't invite → Viewer is read-only.

---

## How this changes the Auth plan ([AUTH_PLAN.md](AUTH_PLAN.md))
- **AUTH-0 allowlist** → becomes the ACC-4 gate (allowlist **or** membership).
- **AUTH-3 `/auth/session`** → after issuing, resolves the user's current account; JWT carries `account_id` + `role`.
- **AUTH-4 `/me/*`** → becomes **account-scoped**: `GET /me/accounts` (list), an account selector
  (`X-Account-Id` header or `/accounts/{id}/…` routes), and resource endpoints scoped to the selected
  account with role enforcement. `POST /me/api-keys` mints keys **for the current account**.
- Net: do **Accounts (ACC-0…ACC-3) before AUTH-4**; ACC-4 merges with AUTH-3.

## Files (new unless noted)
- **Data:** `Entities/AccountEntity.cs`, `Entities/MembershipEntity.cs`, `Enums/AccountRole.cs`;
  `AccountId` on `LinkEntity`/`CustomDomainEntity`/`ApiKeyEntity`; DbContext config; Postgres migration
  (with backfill).
- **Services:** `Accounts/AccountPermissions.cs`, `Accounts/IAccountService.cs`, `Accounts/AccountService.cs`;
  edits to `LinkService`/`ApiKeyService`/`CustomDomainService` for account scoping.
- **Admin:** account-context resolution, `Components/Pages/Members.razor`, `/users` (or `/accounts`)
  superuser create-account UI, role-gating across existing pages.
- **Tests:** permission matrix, account/membership service, migration backfill, account-isolation,
  bUnit member-management + role gating, sign-in-gate.

## Out of scope / future
- Billing/seats per account; per-account custom-domain limits.
- Account-level audit log of member actions.
- SСIM / bulk member import; nested orgs.
- Transferring resources between accounts.

## Risks / to confirm during build
- **Migration backfill** is the riskiest step (orphan API keys with no user need synthesized accounts);
  write it idempotently and test on a seeded DB. No prod data yet, but dev DBs must migrate cleanly.
- **Multi-account users** add a "current account" concept everywhere — keep it explicit (switcher + claim)
  rather than implicit.
- **Role escalation** — every membership mutation must verify the actor outranks the target and can't
  grant a role above their own.

## Suggested order
ACC-0 → ACC-1 (the heavy re-home) → ACC-2 → ACC-3 → ACC-4 (merges with AUTH-3) → ACC-5, then resume the
Auth plan from AUTH-4 (now account-scoped).
