# Security Policy

## Reporting a vulnerability

Please report security vulnerabilities **privately** — do not open a public issue.

Email **[zuechai@gmail.com](mailto:zuechai@gmail.com)** with:

- a description of the issue and its impact,
- steps to reproduce (a proof of concept if you have one), and
- the affected component (`ShortLynx.Web`, `ShortLynx.Core`, `ShortLynx.Admin`) and version/commit.

You'll get an acknowledgement as soon as possible. Please give a reasonable window to
investigate and ship a fix before any public disclosure. Good-faith reports are welcome and
appreciated.

## Supported versions

ShortLynx is pre-1.0 and under active development. Security fixes land on the `main` branch;
self-hosters should track it.

## Security posture

ShortLynx is designed to protect both operators and the people clicking their links:

- **Credentials** — API keys are stored only as keyed HMAC-SHA256 hashes; magic-link and
  refresh tokens as SHA-256 hashes. All credential comparisons are constant-time.
- **Sessions** — passwordless magic-link sign-in (single-use, atomically claimed), short-lived
  JWT access tokens, rotating refresh tokens with reuse detection, and per-IP rate limiting on
  authentication endpoints.
- **Authorization** — account-scoped tenant isolation on every query; role checks
  (Owner/Admin/Member/Viewer) resolved against the database on every write, so a demotion takes
  effect immediately rather than at token expiry.
- **Transport** — CSRF double-submit protection for cookie sessions; HSTS and forwarded-header
  handling behind the edge proxy.
- **Clicker privacy** — IPs stored only as HMAC hashes (secret pepper keys the HMAC, current hour folded in, so the stored value rotates hourly); DNT/GPC honored;
  k-anonymity (k=10) on all breakdowns; aggregate-only exports.

If you find a gap in any of the above, we want to hear about it.
