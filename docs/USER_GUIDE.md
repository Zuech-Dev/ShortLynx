# ShortLynx — User Guide

A practical, example-driven guide to using ShortLynx: shortening links, tracking who clicks, grouping
links into campaigns, generating QR codes, and reading privacy-respecting analytics.

Two ways to use ShortLynx:
- **The dashboard** (the Admin web app) — point-and-click, best for most people. This guide leads with it.
- **The REST API** — for developers who want to create and measure links from code. See
  [For developers: the API](#for-developers-the-api) and the full reference in
  [API_AUTH.md](API_AUTH.md).

Throughout, replace `https://shrtlynx.com` with your own short domain, and `https://app.shrtlynx.com`
with wherever your dashboard is hosted.

---

## 1. Core ideas in 60 seconds

- **A short link** redirects `https://shrtlynx.com/abc123` → your long destination URL, and records the
  click.
- **Two link modes:**
  - **Anonymous** — one shared short code for everyone. Aggregate clicks. Use for social posts, QR
    codes, docs — anywhere lots of people share one link.
  - **User-attributed** — a *separate* code minted per recipient, so you can see *which specific person*
    clicked. Use for email and sales outreach. No login is required from the person clicking.
- **Campaigns** group links so their clicks roll up into one report, and can auto-append UTM tags.
- **Analytics** show clicks over time, by platform, device, browser, OS, and more — derived without
  identifying the person clicking (see [Privacy](#7-privacy-what-we-do-and-dont-collect)).

---

## 2. Signing in

ShortLynx uses **passwordless sign-in**. There are no passwords to remember.

1. Go to your dashboard (e.g. `https://app.shrtlynx.com`).
2. Enter your email and request a sign-in link.
3. Open the email and click the link — you're in.

> Your email must be **allowlisted** by the operator, or you must already be a **member of an account**
> someone invited you to. If sign-in says you're not permitted, ask whoever runs your ShortLynx instance
> to add you.

If you belong to more than one account (e.g. your own plus a client's), use the **account switcher** in
the top bar to change which account you're working in.

---

## 3. Creating links

### Example A — an anonymous link for a social post

*Scenario: you're launching a blog post and want one branded link to share on Bluesky, plus a QR code
for a conference slide.*

1. Dashboard → **Links** → **+ New link**.
2. Paste the destination, e.g. `https://myblog.com/2026/spring-launch`.
3. Leave the type as **Anonymous**.
4. *(Optional)* pick a **Campaign** now, or assign one later (see [Campaigns](#5-campaigns)).
5. **Create link.** You'll get a full short URL like `https://shrtlynx.com/aB3xK9` — click **Copy**.

Share that URL anywhere. Everyone who clicks it is counted together.

### Example B — user-attributed links for an email campaign

*Scenario: you're emailing 200 prospects and want to know exactly who clicked, without cookies or a CRM.*

1. Create a link and choose **User-attributed** as the type.
2. You'll land on the link's detail page. Under **Provision codes**, paste one recipient per line — an
   email or any label you'll recognize:
   ```
   alice@acme.com
   bob@globex.com
   carol@initech.com
   ```
3. **Provision codes.** ShortLynx mints a *unique* short URL for each recipient and shows them all —
   click **Copy all** to grab the list.
4. Put each recipient's unique URL in *their* email (mail-merge style).

Now the link detail page's analytics show **which recipients clicked** — and, just as usefully, who
*didn't*, so you can follow up.

> **One-time-use codes:** tick "One-time use" when provisioning if each link should redirect only once
> (e.g. a single-use download or RSVP). After the first click it stops resolving.

---

## 4. Branding, custom domains, and QR codes

### Custom domains

Serve links from your own domain (`go.yourbrand.com/abc123`) instead of the default.

1. Dashboard → **Domains** → **+ Add domain**, enter `go.yourbrand.com`.
2. ShortLynx shows a **DNS TXT record** to add at your DNS provider. Add it, then click **Verify**.
3. Once verified, open any link → the **Custom domain** card → pick your domain → **Save**. That link now
   resolves on your brand's host.

### QR codes

Every anonymous link has a **QR code** on its detail page. Download **PNG** (for print/embedding) or
**SVG** (scales to any size — billboards, posters). For user-attributed links, each recipient row has its
own **PNG / SVG** download, so you can put a *per-person* QR in, say, an event badge.

QR scans usually arrive with no referrer, so they show up under **Direct** in your analytics.

---

## 5. Campaigns

A **campaign** groups links and rolls their clicks into one report — and can stamp a shared **UTM
template** onto every link's destination so tools like Google Analytics attribute the traffic.

### Example C — a launch campaign across channels

*Scenario: "Spring Launch" runs across Bluesky, a newsletter, and a printed flyer. You want one number
for the whole push, plus per-channel breakdown.*

1. Dashboard → **Campaigns** → **+ New campaign**. Name it `Spring Launch`. Optionally set a UTM
   template:
   - `utm_source` = `spring-launch`
   - `utm_medium` = `social`
   - `utm_campaign` = `2026-spring`
2. Create three links (one per channel) and assign each to **Spring Launch** — either from the **Campaign**
   dropdown on the *New link* form, or later via the **Campaign** card on each link's detail page.
3. When someone clicks, ShortLynx appends the UTM tags to the destination (without overwriting any UTM the
   URL already has), so your site analytics see `?utm_source=spring-launch&utm_medium=social&…`.
4. Open **Campaigns → Spring Launch** for the roll-up: total and unique clicks across all three links,
   the platform/device/time breakdowns, and a **per-link table** so you can see which channel pulled its
   weight.

> Deleting a campaign **does not delete its links** — they're just un-grouped.

---

## 6. Reading your analytics

Open any link's detail page (or a campaign for the roll-up). You'll see a **Click breakdown** card and,
on links, a filterable **Clicks** table.

**The breakdown card shows:**
- **Total clicks** and **Unique clicks** — see the caveat below.
- **First / last click.**
- **By platform** — Twitter/X, Bluesky, LinkedIn, Reddit, Facebook, Instagram, Threads, Mastodon, or
  *Direct* (typed, QR scan, or email).
- **By device** — Mobile / Desktop / Tablet / Bot.
- **By browser, OS, language, and country** — each shown only once there's data for it.
- **Clicks by day** — a chart with a **7 / 14 / 21 / 30-day** toggle; hover any bar for that day's date,
  total, and unique clicks.

**The clicks table** (on link pages) lists individual clicks and lets you **filter** by time frame or date
range, platform, device, and referrer host, and **sort** any column. Great for questions like "did the
Reddit traffic come mostly on mobile?"

> **What "Unique clicks" means.** It counts distinct **hashed** IP addresses, and that hash **rotates
> every hour** on purpose (a privacy measure). So it's best read as *"distinct clickers per hour,
> summed"* — it removes rapid repeat clicks (double-taps, link-preview prefetching) within an hour, not
> lifetime-unique visitors. It's a great relative signal ("Tuesday's post got ~2× the reach of Monday's")
> rather than a precise headcount.

### Reach vs. clicks
ShortLynx measures **clicks**, not how many people *saw* the link. True click-through rate (impressions ÷
clicks) requires connecting your social accounts — that's on the roadmap, not available yet.

---

## 7. Privacy — what we do and don't collect

ShortLynx is built to measure **campaign effectiveness, not to profile the people clicking.**

**We keep, per click:** a one-way **hashed** IP (raw IP is never stored), and low-entropy *buckets*
derived at click time — platform, device class, browser, OS, primary language, referrer **host** (not the
full URL), and optionally country.

**We never store:** the raw IP, the raw User-Agent string, or the full referring URL (its path and query
— which can carry search terms or tokens — are dropped). No tracking cookies are set on the redirect.

**We honor "do not track."** If a visitor's browser sends **DNT** or **Global Privacy Control (Sec-GPC)**,
the click is still counted but **no per-click dimensions are recorded** for them.

This is why some breakdowns show fewer clicks than the total — clicks with an unknown or suppressed
dimension aren't force-bucketed.

---

## 8. For developers: the API

Prefer to create and measure links from code? ShortLynx has a REST API. Full details and auth options
(bearer tokens vs. cookies, refresh, CORS) are in **[API_AUTH.md](API_AUTH.md)**; here's the quick path.

### Get an API key
After signing in, mint an API key from the dashboard (or `POST /me/api-keys`). It's shown **once** —
store it. API keys authenticate the `/links`, `/domains`, and related endpoints.

### Create a link
```bash
curl -X POST https://api.shrtlynx.com/links \
  -H "Authorization: ApiKey sk_live_your_key" \
  -H "Content-Type: application/json" \
  -d '{"url": "https://myblog.com/2026/spring-launch"}'
# → { "id": "...", "shortCode": "aB3xK9", ... }
```

### Read a link's analytics
```bash
curl https://api.shrtlynx.com/links/{id}/analytics \
  -H "Authorization: ApiKey sk_live_your_key"
```
```jsonc
{
  "totalClicks": 1240,
  "uniqueClicks": 847,          // distinct hashed IPs (hourly-rotating — see §6)
  "firstClickAt": "…", "lastClickAt": "…",
  "sources":  [ { "source": "Twitter",  "count": 612 }, … ],
  "devices":  [ { "device": "Mobile",   "count": 901 }, … ],
  "timeline": [ { "date": "2026-06-20", "count": 130 }, … ]
}
```

### Provision a QR code
```bash
# PNG (default) or SVG; size is pixels-per-module
curl "https://api.shrtlynx.com/me/links/{id}/qr?format=svg&size=12" -o link.svg
```

### Building your own frontend?
Use the session (magic-link → JWT) flow and the account-scoped `/me/*` surface — campaigns, members, links,
domains, analytics — documented end-to-end in [API_AUTH.md](API_AUTH.md).

---

## 9. FAQ

**Can I change a link's destination after sharing it?**
The short code is stable; editing destinations isn't exposed in the UI today. Create a new link if the
target changes.

**Why does a breakdown not add up to my total clicks?**
Dimensions are only recorded when known and not suppressed (privacy signal / unknown value), so the
per-browser or per-country counts can sum to less than the total. Totals are always exact.

**Someone clicked but it's not showing yet.**
Clicks are written in the background in small batches, so there can be a brief delay before a very recent
click appears.

**Is self-hosting fully featured?**
Yes — the open-source build is complete and unlimited at every tier. Hosted plans only add managed
operation, not extra features.

**Do the people clicking my links need an account?**
No. Clicking a link — including a user-attributed one — never requires the recipient to log in.
