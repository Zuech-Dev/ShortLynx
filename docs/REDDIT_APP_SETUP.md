# Reddit App Setup & Approval — Operator Guide

Like Meta (see [META_APP_SETUP.md](META_APP_SETUP.md)), this all happens on Reddit's own portals — none
of it is a command this repo can run. The connector code is already built; this checklist gets you the
credentials and the API approval to use it.

---

## 1. Create the Reddit app

1. Sign in to Reddit with the account that should own the app (a dedicated business/brand account is
   recommended — it also becomes the default profile posts publish to when users connect it).
2. Go to **reddit.com/prefs/apps** → **create another app…**:
   - **Type:** `web app` (required for the authorization-code + refresh-token flow the connector uses).
   - **Redirect URI:** `https://shortlynx.dev/social/reddit/callback` — must match `Reddit:RedirectUri`
     exactly. Add a localhost/tunnel URI here later if you want local testing (same drill as Threads).
3. Note the credentials:
   - **Client ID** — the string under the app name (there's no label).
   - **Secret** — labeled.

## 2. Request data-API approval

Since 2023, production use of Reddit's data API requires registration approval:

1. Submit the **Reddit data API access request** (reddit.com → support → "Request access to the Reddit
   API for commercial or research purposes" — the form is linked from
   support.reddithelp.com's Data API articles).
2. Describe the use case factually: "users connect their own Reddit account and publish text posts
   containing their own tracked short links to their own profile; the app reads back score/comment
   counts on those posts." Emphasize: **own profile only, no subreddit posting, no data scraping.**
3. Free tier (≤100 queries/minute per OAuth client) is ample for this workload; note that in the request.
4. Approval reportedly takes ~2–4 weeks, similar to Meta. Development against your **own account**
   works before approval — the same "developers of the app can use it" carve-out as Meta test users
   (add teammates under the app's **developers** list on prefs/apps).

## 3. Set the Railway config

On **both** `admin` and `core` services (Admin runs the OAuth callback; Core refreshes tokens during
publish/metrics):

| Variable | Value |
|---|---|
| `Reddit__AppId` | the client ID from step 1 |
| `Reddit__AppSecret` | the secret — **never commit it**, Railway env / user-secrets only |
| `Reddit__RedirectUri` | `https://shortlynx.dev/social/reddit/callback` |
| `Reddit__UserAgent` | `web:shortlynx:v1.0 (by /u/<your-reddit-username>)` — Reddit **requires** a descriptive UA naming a responsible account and blocks default library values |

## 4. Test

Dashboard → **Social** → **Connect Reddit** → approve on Reddit's consent page (scopes: identity,
submit, read; duration is "permanent" so a refresh token is issued). Then publish an anonymous link from
its detail page — the post lands on the connected account's **own profile** (`u/<name>`) as a text post
with the tracked short URL, and score/comment counts flow back into the published-posts table.

## Deliberate v1 limits

- **Own-profile posts only** (subreddit `u_<username>`). Automated posting into arbitrary subreddits is
  the fastest way to get an account banned under per-subreddit self-promotion rules — if it's ever
  added, it needs per-subreddit rule awareness and explicit user choice, not a default.
- Reddit exposes **no impression/view counts** to app users — score (→ Likes) and comment count
  (→ Replies) only. Same honest-metrics stance as Bluesky/Mastodon.
