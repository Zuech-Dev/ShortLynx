# Chrome Web Store listing — copy-paste reference

Everything below is drafted for the Developer Dashboard's fields. Field names/exact requirements
on the Dashboard drift over time — treat this as source text to adapt, not a guaranteed match to
whatever the current form looks like.

## Store listing tab

**Name:** ShortLynx

**Short description** (132 char max — this one is 95):
> Create ShortLynx short links from any tab. Points at your own self-hosted ShortLynx server.

**Category:** Productivity (Developer Tools is a reasonable alternative — it's a fairly
developer-oriented tool given it needs an API key and a self-hosted server, but the core action,
"shorten this link," is a productivity task)

**Detailed description:**
> ShortLynx is a self-hosted, open-source short-link service. This extension lets you create a
> ShortLynx short link for the page you're on without leaving your tab — no dashboard round trip.
>
> **What it does**
> - Click the toolbar icon to open a small form pre-filled with the current page's URL
> - Choose anonymous (one shared code) or user-attributed (a code per recipient) link mode
> - Optionally set a custom vanity code, or assign the link to a campaign
> - Get your short link back immediately, with a one-click copy button
>
> **Bring your own server**
> This extension doesn't talk to any service run by its developer. You point it at your own
> ShortLynx instance (self-hosted or a ShortLynx Hosted account) and a scoped API key you generate
> yourself from that account's API Keys page. Every request goes only to the server you configure —
> nothing is collected, logged, or sent anywhere else.
>
> **Setup**
> 1. Generate an API key with the `links:write` scope (add `campaigns:read` too if you want the
>    campaign picker) from your ShortLynx account's API Keys page.
> 2. Open this extension's options page, enter your server's URL and the key, and save.
> 3. Click the toolbar icon on any page to create a short link for it.
>
> ShortLynx is open source: https://github.com/Zuech-Dev/ShortLynx
>
> Requires a ShortLynx server (self-hosted or hosted) with an API key — this extension is a client,
> not a standalone service.

**Language:** English

## Privacy practices tab

**Single purpose description:**
> This extension's single purpose is to let a signed-in ShortLynx user create a short link for the
> page they're currently viewing, by calling the API of a ShortLynx server the user has explicitly
> configured.

**Permission justifications:**

| Permission | Why it's needed |
|---|---|
| `storage` | Saves the user's configured server URL and API key locally (`chrome.storage.local`) so they aren't re-entered on every use. Never synced to any remote service controlled by the developer. |
| `activeTab` | Reads the URL of the page the user is currently viewing, only at the moment they open the popup, to pre-fill the destination field. Not used in the background and not used on any tab the user hasn't actively invoked the extension on. |
| Host permission (optional, requested at runtime) | The extension calls the API of the user's own self-hosted ShortLynx server to create links — an address that can't be known in advance since every deployer runs their own instance at their own domain. Permission for that specific origin is requested only once the user enters it on the options page (`chrome.permissions.request`), never as a blanket install-time grant, and never for any domain the user hasn't explicitly configured. |

**Data usage disclosure** (the categories Chrome asks you to check):

- ☑ **Authentication information** — the API key the user pastes into the options page. Stored
  locally; transmitted only to the user's own configured server, as a Bearer header, to authenticate
  create-link requests.
- ☑ **Web history** — the URL of the page the user is on, read via `activeTab` to pre-fill the
  destination field, and sent only to the user's own configured server as the link's destination.
- Everything else (health, financial/payment, personal communications, location, personally
  identifying information beyond the above) — not collected.

For the three certification checkboxes Chrome requires:
- Not being sold to third parties — true, nothing is sold or shared with anyone.
- Not used or transferred for purposes unrelated to the extension's single purpose — true.
- Not used or transferred to determine creditworthiness or for lending — true.

**Privacy policy URL:** see `PRIVACY.md` in this folder — publish it (e.g. as a GitHub Pages page,
or linked to its raw/rendered URL in this repo) and paste that URL here.

## Screenshots

Three are in `screenshots/` (1280×800, real captures of the actual extension, not mockups):
1. `1-create-link.png` — the create-link form
2. `2-result.png` — the success state with the copyable short link
3. `3-self-hosted-setup.png` — the options page, for the "bring your own server" story

Chrome Web Store currently wants at least one screenshot; double-check the exact size/count it
asks for when you're actually on the upload screen, since these requirements have changed before.

## Icons

`public/icon/{16,32,48,128}.png` are already wired into the built extension's manifest — nothing
extra to upload for these; the store listing pulls the 128px one automatically from the package.
