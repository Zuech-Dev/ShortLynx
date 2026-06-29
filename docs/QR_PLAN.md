# QR Code Download — Feature Plan

Give link owners a QR code for any short link, downloadable as **PNG** (raster, easy to embed/print)
or **SVG** (scalable) so they can choose per use-case.

## What the QR encodes
The link's **full short URL**, built with the same precedence the redirect uses:
- pinned to a verified custom domain → `https://<that-domain>/<code>`
- otherwise → `<PublicBaseUrl>/<code>`

Anonymous (Mode-1) links use their single short code. User-attributed (Mode-2) links have a per-recipient
code, so the endpoint takes an optional `?code=` to choose which one (validated against the link).

## Library — QRCoder (MIT)
Added to `ShortLynx.Services`. Generates both formats with **no native dependencies** (works in the slim
Linux container): PNG via `PngByteQRCode` → `byte[]`, SVG via `SvgQRCode` → `string`. ECC level M.

## Service — `IQrCodeService` (ShortLynx.Services/Qr/)
```csharp
public interface IQrCodeService
{
    byte[] GeneratePng(string content, int pixelsPerModule = 10);
    string GenerateSvg(string content, int pixelsPerModule = 10);
}
```
Size clamped to a sane range (2–40 px/module). Registered in Core and Admin DI. Pure/deterministic.

## Core REST endpoint
`GET /me/links/{id}/qr?format=png|svg&size=<n>&code=<optional>` on `MeLinksController` (session-auth,
account-scoped): resolve link → resolve code (Mode-1 short code, or `?code=` for Mode-2) → build full URL →
generate → return as a **download** (`image/png` / `image/svg+xml`, `Content-Disposition: attachment`,
filename `{code}.png|svg`). Default `format=png`; unknown format → 400; link not in account → 404.
New Core config **`Links:PublicBaseUrl`** (the Web redirect base).

## Admin UI
Auth-gated Admin endpoint `GET /qr/{linkId}?format=png|svg` (same `IQrCodeService`, scoped to the signed-in
account) that streams the file. On **LinkDetail.razor**: a "QR code" block with **Download PNG** / **Download
SVG** links + an inline SVG preview; for Mode-2, a QR download next to each minted recipient code.

## Tests
- Service: PNG signature + non-empty; SVG contains `<svg`; deterministic; size honored.
- Core: png → 200 `image/png` attachment + PNG header; svg → `image/svg+xml` + `<svg`; bad format → 400;
  foreign link → 404; unauthenticated → 401.
- Admin: bUnit asserts PNG/SVG download links render with correct hrefs on LinkDetail.

## Docs / config
`docs/API_AUTH.md` `/me/*` table gains the `/qr` row; `DEPLOY.md` gains `Links__PublicBaseUrl`.

## Related fix (prerequisite for a correct QR URL)
Admin reads its public base URL from **`Dashboard:PublicBaseUrl`**, but the Railway `admin` var was set as
`ShortLynx__PublicBaseUrl` — so it currently falls back to bare codes. Correct the Railway var to
`Dashboard__PublicBaseUrl` and align DEPLOY.md.

## Out of scope (v1)
Public Web QR endpoint (`/{code}/qr`, no auth) — deferred; QR stays a dashboard/API feature.

## Commit breakdown
1. QrCodeService + QRCoder + service tests
2. Core `/me/links/{id}/qr` + `Links:PublicBaseUrl` + API tests
3. Admin download endpoint + LinkDetail UI + bUnit
4. Docs + Railway base-URL fix + verify
