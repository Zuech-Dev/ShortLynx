# Phase 7 — Public Web (ShortLynx.Web)

Prerequisites: Phase 4 complete (redirect pipeline logic exists in services).

`ShortLynx.Web` is the public-facing site. Its only required job is handling redirects. The redirect logic from Phase 4 is implemented here (not in `ShortLynx.Core`).

---

## Step 1: Redirect route

Add a catch-all Razor Page or minimal API endpoint:

```
GET /{code}
```

This is the same pipeline described in Phase 4, Step 2. The services (`IDistributedCache`, `IVisitEventSink`, repositories) are shared via DI — register the same services in `ShortLynx.Web/Program.cs` as in `ShortLynx.Core`.

Use a Razor Page named `Redirect.cshtml` with route `@page "/{code}"` so it catches all single-segment paths. Return `RedirectResult` (302) from `OnGetAsync`.

---

## Step 2: Error pages

- `404.cshtml` — "Link not found or no longer active." Minimal, branded.
- `410.cshtml` — "This link has already been used." (for one-time-use codes)
- `429.cshtml` — "Too many requests. Please try again shortly."

Configure custom error pages in `Program.cs` via `UseStatusCodePagesWithReExecute`.

---

## Step 3: Interstitial page (optional)

If `Redirect:Interstitial: true` in config:

Add `Interstitial.cshtml` (`@page "/go/{code}"`):
- Display the destination URL prominently
- Show "You are leaving [your domain]. Continue to [destination]" with a button
- Auto-redirect after 5 seconds (configurable) via a meta refresh or JS timer
- Serve the visit event enqueue on this page load, not on the button click

The redirect endpoint forwards to `/go/{code}` instead of directly issuing a 302.

---

## Step 4: Health endpoint

Add `GET /health` returning `200 OK` with a plain-text or JSON body. Used by Docker health checks and load balancers. Does not require authentication.

Optionally extend to check DB connectivity and cache connectivity.

---

## Step 5: Security headers

Add via middleware in `Program.cs`:
- `X-Frame-Options: DENY`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer` (prevents destination URL leakage via `Referer` header on outbound redirect)
- `Content-Security-Policy`: strict policy for any pages served (interstitial, error pages)

---

## Verification

1. `GET /abc1234` for a valid code → `302` to destination
2. `GET /abc1234` for invalid code → custom `404` page
3. `GET /health` → `200`
4. Check response headers include `X-Frame-Options`, `Referrer-Policy`
5. With interstitial enabled: `GET /abc1234` → interstitial page shown, auto-redirects after timeout

Next: [Phase 8 — Operations](08-operations.md)
