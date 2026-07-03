# Meta (Threads) App Setup & Approval — Operator Guide

Everything in this doc happens on **Meta's own portals** (developers.facebook.com, business.facebook.com)
— none of it is a command this repo can run for you. This is the checklist to work through, in order,
to get from "no Meta app" to "Threads connector approved for production."

Per [SOCIAL_INTEGRATIONS_PLAN.md](SOCIAL_INTEGRATIONS_PLAN.md), Threads is **Phase 2** — gated, ~2–4 weeks
of review lead time. Start this now; the codebase work for the connector itself can proceed in parallel
once you have a **Meta App ID** and the review requirements below are satisfied.

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
   - **User Data Deletion**: either paste the `/DataDeletion` URL (instructions-URL method — simplest,
     no callback to build) or, if you'd rather automate it later, implement the signed-request callback
     protocol (`docs.developer.facebook.com` → Data Deletion Callback) — not required for the instructions
     method.
   - **Category**: something accurate, e.g. "Business" or "Productivity".
5. Add the **Threads** product to the app (App Dashboard → Add Product → Threads API).

Note the **App ID** and **App Secret** — you'll put these in Railway config once the connector code lands
(not yet — that's a follow-up branch after this one).

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

## 6. While waiting on review

Review is the long pole, not engineering — so once the app + permission requests are submitted, the
Threads connector code (`ThreadsConnector : ISocialConnector`, following the exact shape
`BlueskyConnector`/`MastodonConnector` already establish) can be built and tested against Meta's
**test users** (App Dashboard → Roles → Test Users get an approved-permission sandbox without waiting
for full review). That's the natural next branch once you have an App ID + Secret to test against.

---

## 7. Operational notes for later

- **Token storage**: Threads OAuth tokens will use the exact same `SocialConnectionEntity` +
  `ITokenProtector` encryption path already built for Bluesky/Mastodon — no new schema needed, just a new
  `SocialPlatform.Threads` enum value and connector.
- **Config**: App ID/Secret go in Railway as `Meta__AppId` / `Meta__AppSecret` (Core service) when that
  branch lands — never commit them.
- **Re-review**: Meta periodically re-reviews apps with live permissions (especially after material UI
  changes to the connect/publish flow) — keep the screencast and written justifications on file.
