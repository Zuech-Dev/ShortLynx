# ShortLynx Browser Extension — Privacy Policy

_Last updated: 2026-09-06_

## Summary

This extension does not send data to its developer or to any third party. It talks only to the
ShortLynx server address you configure yourself, and stores its settings only on your own device.

## What the extension stores locally

When you fill in the options page, the extension saves the following in your browser's local
extension storage (`chrome.storage.local`), on your device only:

- The server URL you enter
- The API key you enter
- The optional "short link domain" and "custom code route prefix" overrides, if you set them

None of this is synced to any account, cloud service, or analytics platform. It never leaves your
browser except in the requests described below.

## What the extension sends, and to whom

Every network request this extension makes goes only to the server URL you configured. There is no
other destination — no developer-operated server, no analytics endpoint, no crash reporter, no
third-party API.

When you create a link, the extension sends your configured server:
- The destination URL of the page you're shortening
- Your API key, as an `Authorization` header, to authenticate the request
- Optionally: a custom code and/or a campaign ID, if you set them

That's the complete set of data the extension transmits. It doesn't collect analytics, doesn't
track your browsing beyond the single active tab it reads when you open the popup, and doesn't
communicate with any server other than the one you typed into the options page.

## Permissions this extension uses

- **`storage`** — to save your server URL and API key locally, as described above.
- **`activeTab`** — to read the URL of the page you're currently on, so the popup can pre-fill it
  as the link destination. This only happens when you actively open the popup; the extension has no
  background access to your browsing.
- **Host permission for your configured server** (requested at runtime, not granted at install) —
  needed to make authenticated requests to that server. The extension only ever requests this for
  the exact address you enter on the options page, never for any other site, and never as a
  blanket grant covering sites you haven't configured.

## Your ShortLynx server's own practices

Because ShortLynx is self-hosted, the server you point this extension at is operated by you, your
organization, or a ShortLynx Hosted account you control — not by this extension's developer. That
server's own logging, retention, and analytics practices are governed by whoever operates it, not
by this policy. If you're using someone else's ShortLynx deployment, ask them about their practices
for the data a create-link request contains (the destination URL you shorten).

## Open source

This extension's source code is part of the ShortLynx project:
https://github.com/Zuech-Dev/ShortLynx/tree/main/browser-extension

## Changes to this policy

If this policy changes, the updated version will be committed to the same repository with a new
"Last updated" date above.

## Contact

Questions about this policy or the extension should be directed through the ShortLynx GitHub
repository linked above.
