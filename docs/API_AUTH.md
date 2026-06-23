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
| `GET /me/links` `?page=&pageSize=` | List links |
| `POST /me/links` `{ url, mode? }` | Create (`mode`: `Anonymous` default, or `UserAttributed`) |
| `GET /me/links/{id}` | One link |
| `POST /me/links/{id}/codes` `{ userIds:[] }` | Provision user-attributed codes |
| `PUT /me/links/{id}/domain` `{ customDomainId }` | Pin/unpin to a verified domain (null = unpin) |
| `GET /me/links/{id}/analytics` | Click analytics |
| `GET /me/api-keys` · `POST` `{ name, scopes }` · `DELETE /{id}` | Manage API keys (POST returns the plaintext once) |
| `GET /me/domains` · `POST` `{ domain }` · `POST /{id}/verify` · `DELETE /{id}` | Manage custom domains |

> **Bootstrapping machine credentials:** after a user logs in, `POST /me/api-keys` mints an API key for
> their account — that key then authenticates the existing key-scoped endpoints (`/links`, `/domains`, …).

### Roles
`Owner > Admin > Member > Viewer`. Members manage resources; Admins/Owners also manage membership.
Member management lives in the Admin dashboard today; a `/me/members` write surface is a future addition.

---

## 4. Config the operator must set (Core)
- `Jwt:SigningKey` — 32+ char secret (fail-fast at startup).
- `Cors:AllowedOrigins` — your frontend's exact origin(s) for cross-origin use.
- `MagicLink:ConfirmationUrlBase` — your frontend callback URL.
- `Jwt:CookieSameSite` — `Lax` same-site; `None` (with `CookieSecure=true`) for cross-site cookies.
- For local dev where Resend can't deliver to test addresses, set `Email:Mode=Hybrid` +
  `Email:DeliverableDomains` (or `Log`) to read magic links from the console.
