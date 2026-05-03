# Phase 6 — Admin UI (ShortLynx.Admin)

Prerequisites: Phase 5 complete (API endpoints exist). The admin UI calls `ShortLynx.Core` over HTTP — it does not access the DB directly.

**Authentication scope (Q14):** In this phase the admin UI authenticates via a single API key configured in `appsettings.json` (`Api:Key`). There is no browser login page — the assumption is that the admin UI is accessible only on a trusted network or behind a reverse proxy with access control. A browser-based login flow (OAuth2/OIDC) is a future enhancement and out of scope for Phase 6.

---

## Step 1: HTTP client setup

Register a typed `ShortLynxApiClient` in `ShortLynx.Admin/Program.cs`:
- Base URL and API key read from `appsettings.json` under `Api:BaseUrl` and `Api:Key`
- Add `X-Api-Key` header via a `DelegatingHandler`

---

## Step 2: Layout and navigation

The project already scaffolds `MainLayout.razor` and `NavMenu.razor`. Update nav to include:
- Links
- API Keys
- Settings (placeholder for Phase 8)

---

## Step 3: Links list page (`/links`)

Blazor component: `Pages/Links/Index.razor`

- Table: Code, Destination URL, Mode, Status (Active/Inactive), Created, Clicks
- Pagination (server-side)
- "New Link" button → opens `CreateLinkModal`
- Row actions: View, Deactivate, Delete

---

## Step 4: Link detail page (`/links/{id}`)

Blazor component: `Pages/Links/Detail.razor`

- Link metadata (URL, mode, expiry, status)
- Mode 1: aggregate analytics chart (clicks over time, top referrers)
- Mode 2: user code table with per-user click totals, export to CSV
- "Add User Codes" action (opens modal, accepts newline-separated UUID list)

---

## Step 5: Create link modal

`Components/CreateLinkModal.razor`

Fields:
- Destination URL (validated client-side: must start with `https://`)
- Mode: Anonymous / User-Attributed (radio)
- Expiry date (optional date picker)

On submit: calls `POST /links`, closes modal, refreshes list.

---

## Step 6: API key management page (`/api-keys`)

- Table: Name, Prefix, Scopes, Expires, Status
- "Create API Key" button → shows generated plaintext key in a one-time reveal dialog (copy-to-clipboard, cannot be retrieved again)
- Revoke button per row

---

## Verification

1. Navigate to `/links` → list loads, pagination works
2. Create a link via the UI → appears in list, redirect works
3. Deactivate a link → subsequent redirect returns `404`
4. View Mode 2 link detail → user code table populates
5. Create API key → plaintext shown once, prefix visible in list, key validates against API

Next: [Phase 7 — Public Web](07-web-public.md)
