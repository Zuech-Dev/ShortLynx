# ShortLynx API — Authentication & the `/me` Surface

A guide for building your own frontend (Next.js, SvelteKit, mobile, …) against the **Core** API. The
Blazor Admin app is one frontend; this is how to bring your own.

Auth is **passwordless (magic link)**. There are two credential transports — pick one:
- **Bearer / JWT** (recommended for cross-origin SPAs and native apps) — you store the tokens and send
  the access token in an `Authorization: Bearer …` header.
- **Cookies** (for a frontend served same-site with the API) — the browser holds httpOnly cookies and
  you send a CSRF header on writes.

Both come from the same login. The access token is a short-lived JWT (~15 min); the refresh token is
long-lived (~30 days), rotated on every use, and revocable.

---

## 1. Login flow

```
POST /auth/magic-link        { "email": "user@example.com" }      → 204 (always; no enumeration)
   … user clicks the emailed link, which points at YOUR frontend callback (see ConfirmationUrlBase) …
   … your callback reads ?token=… from the URL and exchanges it …
POST /auth/session           { "token": "<magic-link-token>" }    → 200 SessionResponse
```

`POST /auth/session` is gated: the email must be **allowlisted** or already a **member of an account**
(otherwise `403`). On success:

```jsonc
// 200
{
  "accessToken": "<JWT>",
  "refreshToken": "<opaque>",
  "expiresIn": 900,                       // seconds
  "user": { "id": "...", "email": "...", "isAdmin": false,
            "accountId": "...", "role": "Owner" }
}
```

It also sets httpOnly cookies (`sl_access`, `sl_refresh`) and a non-httpOnly `sl_csrf` cookie.

> **Where does the email link point?** Set `MagicLink:ConfirmationUrlBase` (Core config) to **your
> frontend's** callback route, e.g. `https://app.example.com/auth/callback`. The link becomes
> `…/auth/callback?token=…`; your page POSTs that token to `/auth/session`.

### The JWT claims
`sub` (user id), `email`, `account_id` (current account), `role` (Owner/Admin/Member/Viewer),
`is_admin` (platform super-admin). Every session is scoped to exactly one **current account**.

---

## 2. Using the session

**Cross-origin (Bearer):** keep `accessToken` in memory, `refreshToken` wherever you store secrets, and
send `Authorization: Bearer <accessToken>` on every request. No CSRF needed (the header isn't sent
automatically by browsers). Configure CORS via `Cors:AllowedOrigins`.

**Same-site (cookies):** rely on the cookies (don't read the body tokens). On every **unsafe** request
(POST/PUT/PATCH/DELETE) you MUST send `X-CSRF-Token: <value of the sl_csrf cookie>`, or you get `403`.
Safe GETs need nothing.

### Refresh & logout
```
POST /auth/refresh   { "refreshToken": "…" }   (or omit body to use the cookie)   → 200 { accessToken, refreshToken, expiresIn }
POST /auth/logout    { "refreshToken": "…" }   (or omit body to use the cookie)   → 204
GET  /auth/me                                                                      → 200 (current user)
```
Refresh **rotates** the refresh token (use the new one). Replaying an old refresh token revokes the
whole chain (reuse detection) — re-login.

---

## 3. The `/me/*` surface (account-scoped)

All require a session and act on the JWT's **current account**.

| Method & path | Purpose |
|---|---|
| `GET /me` | Current user + account + role |
| `GET /me/accounts` | Accounts you belong to (for an account switcher) |
| `GET /me/members` | Members of the current account |
| `POST /me/members` `{ email, role }` | Invite a member (you may grant only roles below your own) |
| `PUT /me/members/{userId}` `{ role }` | Change a member's role (you must outrank them) |
| `DELETE /me/members/{userId}` | Remove a member (you must outrank them) |
| `GET /me/links` `?page=&pageSize=` | List links |
| `POST /me/links` `{ url, mode? }` | Create (`mode`: `Anonymous` default, or `UserAttributed`) |
| `GET /me/links/{id}` | One link |
| `POST /me/links/{id}/codes` `{ userIds:[] }` | Provision user-attributed codes |
| `PUT /me/links/{id}/domain` `{ customDomainId }` | Pin/unpin to a verified domain (null = unpin) |
| `PUT /me/links/{id}/campaign` `{ campaignId }` | Assign/unassign the link to one of your campaigns (null = unassign) |
| `GET /me/links/{id}/analytics` | Click analytics (see the shape below) |
| `GET /me/links/{id}/qr` `?format=png\|svg&size=&code=` | Download a QR code of the link's short URL (PNG default; `code=` selects a recipient code for user-attributed links) |
| `GET /me/campaigns` · `POST` `{ name, description?, utmSource?, utmMedium?, utmCampaign? }` | List / create campaigns |
| `GET /me/campaigns/{id}` · `PUT` `{ name, … }` · `DELETE /{id}` | Get / update / delete a campaign (delete unassigns its links, doesn't delete them) |
| `GET /me/campaigns/{id}/analytics` | Campaign roll-up: clicks across all its links + per-link table |
| `GET /me/social` · `POST` `{ platform, identifier, secret, instanceUrl? }` · `DELETE /{id}` | Connect social accounts (Bluesky handle + app password; Mastodon instance URL + access token). Credentials are verified with the platform; tokens are stored encrypted and **never returned**. Errors: 400 bad credentials/platform, 402 plan, 502 platform unreachable. Threads is OAuth-only (gated by Meta App Review) — `POST` with `platform: "Threads"` always 400s; connect it from the dashboard's **Social** page instead, which redirects through Meta's consent screen. |
| `POST /me/links/{id}/publish` `{ connectionIds:[], text? }` | Post the link to connected accounts (anonymous links only). The short URL is appended to `text` automatically. Returns **per-connection results** — partial failure is normal (e.g. one platform down). |
| `GET /me/links/{id}/posts` | Publishing history for the link, with post URLs and pulled engagement metrics (likes/reposts/replies; impressions only on platforms that report them — Bluesky/Mastodon don't) |
| `POST /me/links/{id}/posts/refresh` | Pull current engagement metrics for the link's posts now, then return them (they also refresh automatically on a schedule) |
| `GET /me/api-keys` · `POST` `{ name, scopes }` · `DELETE /{id}` | Manage API keys (POST returns the plaintext once) |
| `GET /me/domains` · `POST` `{ domain }` · `POST /{id}/verify` · `DELETE /{id}` | Manage custom domains |

### Analytics payload

`GET /me/links/{id}/analytics` (and the API-key `GET /links/{id}/analytics`) return totals plus
breakdowns derived from data already captured at click time — **no clicker identity is exposed**:

```jsonc
{
  "totalClicks": 1240,
  "uniqueClicks": 847,          // distinct hashed IPs (see caveat)
  "firstClickAt": "…", "lastClickAt": "…",
  "codes": [ { "code": "…", "userId": null, "clickCount": 1240 } ],
  "sources":  [ { "source": "Twitter",  "count": 612 }, … ],   // platform from referrer
  "devices":  [ { "device": "Mobile",   "count": 901 }, … ],   // coarse class from user-agent
  "timeline": [ { "date": "2026-06-20", "count": 130 }, … ]    // clicks per UTC day
}
```

`uniqueClicks` counts distinct hashed IPs. The IP hash **rotates hourly by design** (a privacy measure
that limits cross-time linkage), so this is "distinct clickers per hour, summed" — it dedupes rapid
repeat clicks within an hour, not lifetime unique visitors. `sources`/`devices` are low-cardinality
buckets (`ClickSource`/`DeviceType`), not raw referrer or user-agent strings.

`GET /me/campaigns/{id}/analytics` returns the same `sources`/`devices`/`timeline` aggregated across
**every link in the campaign** (both anonymous and user-attributed), plus a `links[]` array of per-link
`{ linkId, url, mode, totalClicks, uniqueClicks }`. Campaign-wide `uniqueClicks` dedupes across links
(one visitor clicking two links in the campaign counts once).

#### UTM template
A campaign's optional `utmSource`/`utmMedium`/`utmCampaign` form a default template **merged into the
destination** of each assigned link at redirect time. Existing query params are preserved and a UTM the
destination URL already sets is never overwritten.

> **Bootstrapping machine credentials:** after a user logs in, `POST /me/api-keys` mints an API key for
> their account — that key then authenticates the existing key-scoped endpoints (`/links`, `/domains`, …).

### Roles
`Owner > Admin > Member > Viewer`. Members manage resources; Admins/Owners also manage membership.
Member writes are permission-gated server-side: you can only act on members **below** your own role and
grant roles **below** your own — otherwise the call returns `403` (or `400` for an invalid role/email).
The Admin dashboard offers the same operations with a UI.

---

## 4. The `/admin/users` surface (platform super-admins only)

Cross-tenant user management, gated by the `is_admin` claim (non-admins get `403`). Use this to onboard
users, move them between accounts, and disable accounts. Account-scoped membership management lives under
`/me/members` (above); this is the platform-wide equivalent and is **not** bounded by a single account.

| Method & path | Purpose |
|---|---|
| `GET /admin/users` `?page=&pageSize=` | List users with their account memberships |
| `POST /admin/users` `{ email, accountId?, role?, accountName? }` | Add a user. With `accountId` (must exist) → add to it at `role` (default Member). Without it → create a new account (`accountName` or the email) owned by the user. |
| `PUT /admin/users/{id}` `{ isActive?, isAdmin? }` | Toggle active and/or super-admin (omitted fields unchanged) |
| `DELETE /admin/users/{id}` | **Soft delete** — deactivate (blocks sign-in, keeps the record + memberships) |
| `PUT /admin/users/{id}/accounts/{accountId}` `{ role }` | Assign to an existing account or change the role (upsert) |
| `DELETE /admin/users/{id}/accounts/{accountId}` | Remove the user's membership in an account |

Assigning to a non-existent account returns `404`. A deactivated user can't sign in even with a valid,
unexpired magic-link token; re-enable with `PUT {id} { "isActive": true }`. The same operations are
available in the Admin dashboard's **Users** page (super-admin only).

---

## 5. Config the operator must set (Core)
- `Jwt:SigningKey` — 32+ char secret (fail-fast at startup).
- `Cors:AllowedOrigins` — your frontend's exact origin(s) for cross-origin use.
- `MagicLink:ConfirmationUrlBase` — your frontend callback URL.
- `Jwt:CookieSameSite` — `Lax` same-site; `None` (with `CookieSecure=true`) for cross-site cookies.
- For local dev where Resend can't deliver to test addresses, set `Email:Mode=Hybrid` +
  `Email:DeliverableDomains` (or `Log`) to read magic links from the console.
