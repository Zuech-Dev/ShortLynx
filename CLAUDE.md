# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

**Build:**
```bash
dotnet build ShortLynx.slnx
```

**Run individual apps:**
```bash
dotnet run --project ShortLynx.Core/ShortLynx.Core.csproj       # REST API  → http://localhost:5129 / https://localhost:7271
dotnet run --project ShortLynx.Admin/ShortLynx.Admin.csproj     # Admin UI  → http://localhost:5201 / https://localhost:7067
dotnet run --project ShortLynx.Web/ShortLynx.Web.csproj         # Public UI → http://localhost:5071 / https://localhost:7158
```

No test projects exist yet.

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
| `ShortLynx.Models` | EF Core entities and shared data structures |
| `ShortLynx.Repository` | Data access layer, `IDbOperations` abstraction for bulk ops |
| `ShortLynx.Services` | Business logic; `IShortCodeGenerator`, `IVisitEventSink` interfaces |
| `ShortLynx.Core` | ASP.NET Core REST API (link creation, redirect, analytics) |
| `ShortLynx.Admin` | Blazor Server admin dashboard |
| `ShortLynx.Web` | Razor Pages public-facing site (handles redirects) |

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

### Current state

Most projects contain only placeholder `Class1.cs` stubs. DESIGN.md is the authoritative spec for entities, API surface, and decisions still pending (Redis dependency, data retention policy, one-time-use vs multi-use codes, custom domains).
