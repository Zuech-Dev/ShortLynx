# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

**Build:**
```bash
dotnet build ShortLynx.slnx
```

**Test:**
```bash
dotnet test ShortLynx.slnx
```

**Run individual apps:**
```bash
dotnet run --project ShortLynx.Core/ShortLynx.Core.csproj       # REST API  → http://localhost:5129 / https://localhost:7271
dotnet run --project ShortLynx.Admin/ShortLynx.Admin.csproj     # Admin UI  → http://localhost:5201 / https://localhost:7067
dotnet run --project ShortLynx.Web/ShortLynx.Web.csproj         # Public UI → http://localhost:5071 / https://localhost:7158
```

**Run the whole stack in Docker:**
```bash
cp .env.example .env    # fill in the secrets it lists
docker compose up -d
```

`ShortLynx.Web` and `ShortLynx.Admin` sit behind compose profiles, so a deployer bringing their own
front end can opt out of either (`COMPOSE_PROFILES=web` keeps redirects, drops the dashboard). See
DEPLOY.md — dropping `web` stops short links resolving, since it is the only app that serves
`/{code}`.

## Frontend build (Tailwind CSS)

`ShortLynx.Admin` and `ShortLynx.Web` each compile `wwwroot/css/tailwind.css` from
`Styles/app.tailwind.css` via a standalone CLI at `tools/tailwindcss` (git-ignored, one per machine).
This runs automatically before every `dotnet build`/`dotnet test` if that binary exists.

**The binary's version must match the version pinned in `.github/workflows/ci.yml`'s `tailwind` job**
(currently `v4.3.2`, mirrored in each `.csproj`'s `TailwindVersion` property). CI regenerates the CSS
with that exact version and fails the PR on any diff from the committed file — including a pure
version-string difference, since different Tailwind versions format output slightly differently even
for identical input. If you ever need to (re)fetch the CLI, use the pinned URL the `WarnTailwindMissing`
build warning prints (or `.github/workflows/ci.yml`), never `/releases/latest/...`.

**Always run the CLI from *within* the project directory** (`cd ShortLynx.Admin && ./tools/tailwindcss -i Styles/app.tailwind.css -o wwwroot/css/tailwind.css`),
never from the repo root: Tailwind v4 auto-detects sources relative to the CLI's working directory, and
CI runs from the project dir — a repo-root regen scans the whole repo and bakes other projects' utilities
into the CSS, which then fails CI's staleness check with mysterious extra lines.

**When you change any Razor/`.cshtml` markup that affects classes used** (Admin or Web), regenerate and
commit the CSS as the very last step before committing/pushing — with no `dotnet build`/`dotnet test` in
between the regen and the `git add`/`git commit`. Running the build again in between re-invokes the
same MSBuild target and can silently produce different output if `tools/tailwindcss` is ever on a
different version than what you just used, undoing a correct regen without any visible error.

## Architecture

ShortLynx is a self-hosted .NET 10 short-link service with two link modes:
- **Anonymous links**: one short code per URL, aggregate click tracking (no user identity)
- **User-attributed links**: unique short code minted per user per destination, enabling per-user click attribution without requiring login at redirect time (used for email/sales tracking)

### Projects

| Project | Role |
|---|---|
| `ShortLynx.Data` | EF Core entities, enums, and `ShortLynxDbContext` |
| `ShortLynx.Models` | **Empty** — holds only a `.csproj`. Entities live in `ShortLynx.Data`; don't add them here without moving the rest. |
| `ShortLynx.Repository` | Data access layer, `IDbOperations` abstraction for bulk ops |
| `ShortLynx.Services` | Business logic; `IShortCodeGenerator`, `IVisitEventSink` interfaces |
| `ShortLynx.Core` | ASP.NET Core REST API (links, auth, analytics, live click feed) — **always required** |
| `ShortLynx.Admin` | Blazor Server admin dashboard — the default front end, replaceable |
| `ShortLynx.Web` | Razor Pages public site — serves `/{code}` redirects. Default front end, replaceable, but nothing else serves redirects |
| `ShortLynx.Tests` | The whole test suite — API integration (`WebApplicationFactory`), services, data |

Database migrations live in separate per-provider projects (PostgreSQL default, SQLite for dev). Provider wiring is isolated to the composition root via an `AddShortLynxDatabase()` extension method.

### Redirect pipeline

Rate limit by IP → in-memory cache lookup → 302 redirect response → async visit event enqueue via `System.Threading.Channels` → background `IHostedService` batches writes via `IVisitEventSink`.

### Key interfaces to implement

- `IShortCodeGenerator` — pluggable code generation (hash-based deterministic for Mode 2, random Base62 for Mode 1)
- `IVisitEventSink` — abstracts the visit write path (in-process default; swappable for Hangfire/RabbitMQ)
- `IDbOperations` — bulk DB operations abstraction (needed for efficient batch visit writes)
- `ICacheProvider` — may replace direct Redis dependency

### IDs and short codes

- All primary keys use `Guid.CreateVersion7()` (sequential GUIDs, .NET 9+)
- Short codes are decoupled from entity IDs — never derive the short code from the PK

### Live click feed

`GET /me/stream` is an account-scoped SSE feed of clicks as they land; `GET /me/clicks` returns the
same row shape for a historical window, so a client merges the two into one list.

It **tails the visits table** rather than subscribing to `IVisitEventSink`. That looks like the
obvious wiring and cannot work: redirects are served by `ShortLynx.Web`, a different process from the
API a dashboard talks to, so an in-process fan-out reaches nothing in production while working
perfectly in a single-process dev run.

The cursor is the subtle part. `ClickedAt` is stamped at redirect time but the row commits up to a
batch later, so rows do **not** become visible in `ClickedAt` order — a click can appear behind a
high-water mark already advanced past it. Each poll re-queries a window behind the cursor and
de-duplicates by id. Polling strictly forward loses those clicks silently and permanently.

### Current state

Feature-complete against DESIGN.md's core spec, with a substantial test suite in `ShortLynx.Tests`
(API integration tests via `WebApplicationFactory`, plus service and data tests). DESIGN.md's original
"Still To Be Decided" list is mostly resolved now (see the struck-through items there and the pointers
to where each was decided) — Redis, retention, one-time-use codes, and custom domains all shipped.
DESIGN.md remains the reference for entities and the API surface; treat its still-open items (short
code length/bit-layout specifics, click-dedup strategy, a Bloom-filter pre-flight check) as the actual
remaining gaps, not the whole original list.

Beyond this repo, the hosted product also runs a private billing layer and a second (Next.js)
dashboard — see `ShortLynx.Hosted` (private repo) — that this OSS repo's docs don't cover.

