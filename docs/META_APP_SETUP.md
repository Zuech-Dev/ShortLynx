# Meta (Threads) App Setup & Approval — Operator Guide

Everything in this doc happens on **Meta's own portals** (developers.facebook.com, business.facebook.com)
— none of it is a command this repo can run for you. This is the checklist to work through, in order,
to get from "no Meta app" to "Threads connector approved for production."

Per [SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md), Threads is **Phase 2** — gated, ~2–4 weeks
of review lead time. **The codebase side is done** (`ThreadsConnector`, the OAuth authorize/callback
routes, and the two Meta webhooks are all built and tested — `feature/threads-connector`); what's left is
entirely on Meta's portals: creating the app, verifying the business, and getting the permissions approved.

---

## 1. Prerequisites this repo now provides

Meta's App Review form requires these to be **live, hosted URLs** before you can submit anything for
review. Both now exist (`feature/meta-app-review-prep` → merge before you start the Meta side):

| Requirement | URL |
|---|---|
| Privacy Policy | `https://<your-web-domain>/Privacy` |
| Data Deletion Instructions | `https://<your-web-domain>/DataDeletion` |

Confirm both render correctly on the **deployed** `web` service (not just locally) before step 3 — Meta
spot-checks these URLs.

---

## 2. Create the Meta Developer account + Business Portfolio

1. Go to **developers.facebook.com** and sign in with (or create) a Facebook account you control long-term
   — this becomes the account of record for the app. A dedicated account for the business, not a personal
   one, is strongly recommended.
2. Go to **business.facebook.com** and create a **Business Portfolio** (formerly "Business Manager") for
   ShortLynx / your company. You'll need:
   - Legal business name and address.
   - A business email you control (matching your domain, e.g. `you@shrtlynx.com`, is preferred).
3. Meta may ask for business verification documents (business registration, tax ID) at this stage or
   later during Tech-Provider Verification — have them ready.

---

## 3. Create the Meta App

1. In the [Meta App Dashboard](https://developers.facebook.com/apps), **Create App**.
2. App type: **Business** (this is what unlocks Threads API access as of the current Meta developer
   platform).
3. Link the app to the Business Portfolio from step 2.
4. Under **App Settings → Basic**:
   - **App Domains**: your production domain (e.g. `shrtlynx.com`).
   - **Privacy Policy URL**: the `/Privacy` URL from step 1.
   - **User Data Deletion**: the `/DataDeletion` URL (instructions-URL method) — or leave it for the
     callback URL below, now that it's a real, working endpoint.
   - **Category**: something accurate, e.g. "Business" or "Productivity".
5. Add the **Threads** product to the app (App Dashboard → Add Product → Threads API), then under its
   **Settings**, fill in the three callback fields with the endpoints this repo now serves on the Admin
   app (`shortlynx.dev` — the dashboard, not the public redirect domain):

   | Field | Value |
   |---|---|
   | Redirect Callback URL(s) | `https://shortlynx.dev/social/threads/callback` |
   | Uninstall Callback URL | `https://shortlynx.dev/webhooks/threads/deauthorize` |
   | Delete Callback URL | `https://shortlynx.dev/webhooks/threads/delete` |

   > **The redirect URI field must be submitted as a real URL, not just typed text.** After pasting it,
   > press Enter/Tab so it becomes a chip/pill before saving — a common Meta-dashboard gotcha is the form
   > silently not registering a value that's still sitting as plain text in the input.

6. Note the **App ID** and **App Secret** — see the Railway config section below.

---

## 4. Tech-Provider Verification

This is the gate that unlocks live (non-test-user) API access.

1. App Dashboard → **App Review → Requests**, or **Business Settings → Business Info** — Meta surfaces
   this as **"Complete Tech Provider Verification"** once the app exists.
2. You'll submit:
   - Business verification (may already be satisfied from step 2).
   - Confirmation your app's use case matches what you're requesting (publishing + reading insights on
     behalf of the business's own connected Threads accounts).
3. This step alone can take **1–2 weeks**. Submit it first — the per-permission review in step 5 can
   often be prepared in parallel but won't be *approved* until this clears.

---

## 5. Request the specific permissions (App Review)

Once Tech-Provider Verification is in progress or complete, request these **permissions** under
**App Review → Permissions and Features**:

| Permission | Why ShortLynx needs it |
|---|---|
| `threads_basic` | Base requirement for any Threads API access. |
| `threads_content_publish` | Publish the tracked short link as a Threads post on the connected account's behalf. |
| `threads_manage_insights` | Read post-level metrics (views/likes/replies) so we can compute true CTR. |

For each, Meta requires:
- A **screencast** showing the exact flow in your app (connect account → compose → publish → see the
  post) — record this once the connector UI exists.
- A written explanation of **why** the permission is needed (keep it factual: "lets a user publish a
  link they created in ShortLynx directly to their own connected Threads account, and see how many
  people viewed that post").
- Confirm the **Data Deletion** and **Privacy Policy** URLs are filled in (step 3) — review is blocked
  without them.

Expect **~2–4 weeks** total for this stage, consistent with the plan doc's estimate. Reddit's pre-approval
(Phase 2's other gated platform) runs on a similar timeline but a separate process — see
[SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md) Phase 2 when you're ready to start that one.

---

## 6. Set the Railway config, then test with a Meta test user

Once you have an **App ID** and **App Secret** (available immediately after app creation — you don't need
to wait for Tech-Provider Verification to finish to start testing), set these on **both** the `core` and
`admin` Railway services (both run `ThreadsConnector` — Admin serves the OAuth callback, Core can also
publish/pull metrics via its `/me/*` API):

| Variable | Value |
|---|---|
| `Meta__AppId` | the App ID from the dashboard |
| `Meta__AppSecret` | the App Secret — **never commit this**, Railway env only |
| `Meta__RedirectUri` | `https://shortlynx.dev/social/threads/callback` (must exactly match what's in the Meta dashboard) |

Then, **before** full App Review completes, you can test the whole flow end-to-end using a **Meta test
user** (App Dashboard → Roles → Test Users) — test users get an approved-permission sandbox without
waiting for review:
1. Sign in to the dashboard → **Social** → **Connect Threads** → approve on Meta's consent screen with
   the test user's Threads account.
2. Open any anonymous link → **Post to social** → tick the Threads connection → post. You should get a
   working `threads.net` permalink back, same as the Bluesky/Mastodon flow.
3. This is also the moment to **record the screencast** App Review asks for (§5) — the connect → compose
   → publish → view-post loop, in one recording.

---

## 7. Operational notes

- **Token storage**: Threads OAuth tokens use the exact same `SocialConnectionEntity` +
  `ITokenProtector` encryption path as Bluesky/Mastodon — no separate secret store.
- **Uninstall/delete webhooks are live**: if a user removes ShortLynx from their Threads app settings, or
  requests deletion via Meta's own UI, `POST /webhooks/threads/deauthorize` / `/webhooks/threads/delete`
  fire automatically and delete the matching `SocialConnection` — no manual cleanup needed.
- **Re-review**: Meta periodically re-reviews apps with live permissions (especially after material UI
  changes to the connect/publish flow) — keep the screencast and written justifications on file.
