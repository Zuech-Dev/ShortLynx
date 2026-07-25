# Contributing to ShortLynx

How this codebase is put together, the patterns it commits to, and how to add to it without eroding
them. Read the [Architecture](#architecture) and [Separation of concerns](#separation-of-concerns)
sections before your first change — most review friction comes from a feature landing in the wrong
layer, which is expensive to unpick later.

For build/run commands and the Tailwind workflow, see [CLAUDE.md](CLAUDE.md). For deployment, see
[DEPLOY.md](DEPLOY.md). For the original design rationale, see [DESIGN.md](DESIGN.md) and
[ShortLynx.Development/](ShortLynx.Development/).

> **CLAUDE.md's architecture table has drifted from reality.** Where the two disagree, this document
> is the one that was checked against the code. Specifically: entities live in `ShortLynx.Data`, not
> `ShortLynx.Models` (which is empty); there is no SQLite migrations project; and the default
> configured provider is SQLite, not PostgreSQL. Fixing that table is a welcome first PR.

---

## Architecture

Nine projects. The dependency graph is strictly acyclic and deliberately shallow:

```
                    ┌──────────────────────────────────────┐
                    │  ShortLynx.Core   (REST API)         │
                    │  ShortLynx.Admin  (Blazor Server)    │  ← three independent apps
                    │  ShortLynx.Web    (Razor + redirect) │
                    └──────────────┬───────────────────────┘
                                   │ each references all four below
              ┌────────────────────┼────────────────────┬─────────────────────┐
              ▼                    ▼                    ▼                     ▼
      ShortLynx.Services    ShortLynx.Repository   ShortLynx.Data   ShortLynx.Data.PostgreSql
      (business logic,      (EfCoreDbOperations,   (entities,        (migrations only)
       seam interfaces)      bulk writes)           DbContext,
              │                    │                enums)
              └────────────────────┴────────────────────┘
                                   ▼
                            ShortLynx.Data
```

| Project | Holds | Depends on |
|---|---|---|
| `ShortLynx.Data` | `ShortLynxDbContext`, `Entities/`, `Enums/`, `IDbOperations` | — |
| `ShortLynx.Repository` | `EfCoreDbOperations` — the bulk-write implementation | Data |
| `ShortLynx.Services` | All business logic + the seam interfaces | Data |
| `ShortLynx.Data.PostgreSql` | EF migrations *only*, no logic | Data |
| `ShortLynx.Core` | REST API — MVC controllers, API-key + JWT auth | Data, Repository, Services, Data.PostgreSql |
| `ShortLynx.Admin` | Blazor Server dashboard, magic-link auth | same |
| `ShortLynx.Web` | Public site + the `/{code}` redirect hot path | same |
| `ShortLynx.Tests` | All 780 tests, every layer | Admin, Core, Data, Repository, Services |
| `ShortLynx.Models` | **Empty.** Zero files, zero references. | — |

`ShortLynx.Models` is a vestigial scaffold project. It is still `IsPackable`, so an empty NuGet
package gets published on every `pkg-v*` tag. Deleting it (and dropping it from `Directory.Build.props`'s
packable set) is a clean, self-contained PR — just coordinate first, since a downstream consumer may
reference the package name even though it contains nothing.

### The three apps are siblings, not a stack

`Core`, `Admin`, and `Web` never reference each other. They share code only through `Services`/`Data`.
That is what makes them independently deployable, and what lets an out-of-repo build compose them
differently. Do not add an app-to-app project reference — if two apps need the same behaviour, it
belongs in `Services`.

The cost of that independence is **triplicated composition roots**: each app has its own
`Extensions/ServiceExtensions.cs` with its own `AddShortLynxDatabase` / `AddShortLynxServices`. They
are similar but genuinely not identical — `Admin` registers `AddDbContextFactory` (Blazor components
need to create their own contexts) *plus* a scoped `ShortLynxDbContext` bridged off that factory,
while `Core` and `Web` register a scoped context directly. **When you add a service registration,
check all three.** Forgetting one produces a runtime DI failure in only one app, which is easy to
miss locally and expensive to find in CI.

---

## Separation of concerns

Four rules carry most of the weight. They are worth internalising because the codebase is already
consistent about them, and inconsistency is what makes a codebase hard to change.

**1. Layers only ever point inward.** `Services` may not know about HTTP, Razor, or Blazor.
`Data` may not know about `Services`. If a service needs the current request's user, it takes an
`accountId`/`userId` parameter — it never reaches for `IHttpContextAccessor`. That is why the same
`LinkService` serves the API, the dashboard, and the tests unchanged.

**2. ASP.NET types stop at the app boundary.** `Services` deliberately avoids referencing ASP.NET so
it can be consumed outside a web host. `JwtOptions` in `ShortLynx.Services/Auth` is the worked
example: cookie settings are primitives (`string CookieSameSite`, `bool CookieSecure`) rather than
`SameSiteMode`/`CookieSecurePolicy`, precisely so the options class can live in `Services` while the
app translates them into framework types.

**3. Providers are a composition-root concern.** Only `AddShortLynxDatabase` knows whether this is
PostgreSQL or SQLite. Nothing in `Services` or `Data` branches on provider — *except* where EF Core's
SQLite provider genuinely cannot translate a query, and then it is done explicitly and commented (see
[Provider-specific queries](#provider-specific-queries)).

**4. Migrations live in the provider project, never in `Data`.** `ShortLynx.Data` defines the model;
`ShortLynx.Data.PostgreSql` holds the generated migrations and nothing else.

---

## The patterns you need to know

### Seam interfaces (the extension points)

`ShortLynx.Services` defines ~22 interfaces. Most are ordinary service abstractions, but a handful are
true *seams* — deliberately swappable so behaviour can change without touching call sites:

| Seam | Why it exists |
|---|---|
| `IEntitlements` | Quota/feature gating. Ships as `UnlimitedEntitlements` (self-hosters get everything, free). An out-of-repo build substitutes a billing-backed implementation. |
| `IVisitEventSink` | The visit write path. `InMemoryVisitEventSink` (Channel + background drain) is the default; swappable for a queue. |
| `IDbOperations` | Bulk inserts, so the hot path isn't doing per-row EF change tracking. |
| `IShortCodeGenerator` | Code generation strategy per link mode. |
| `IEmailSender`, `IDnsResolver`, `IGeoIpResolver`, `ITokenProtector` | External I/O, stubbed wholesale in tests. |

**`TryAddSingleton` is load-bearing.** `Core` and `Admin` register `IEntitlements` with
`TryAddSingleton`, not `AddSingleton`. That lets a composition root register its own implementation
*before* calling `AddShortLynxServices()` and win, with no change to this repo. If you convert one of
these to `AddSingleton`, you silently break that override. Keep `TryAdd*` for anything that is a seam.

### Authorization: two schemes, two gates

Two authentication schemes coexist, and picking the wrong base class is the most common security
mistake available here:

- **API keys** (`ApiKeyAuthHandler`) — machine clients. Controllers use
  `[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]` and gate per-endpoint with
  `[RequireScope(Scopes.LinksWrite)]`. Scopes are a fixed list in `ShortLynx.Services/ApiKeys/Scopes.cs`.
- **User sessions** (JWT, `SessionControllerBase`) — the `/me/*` surface. Derive from
  `SessionControllerBase` to get `AccountId`/`CurrentUserId` and `[ValidateSessionClaims]`.

For `/me/*`, authentication is not authorization. **Every mutating `/me/*` endpoint must also carry
`[RequireAccountAction(...)]`**, which resolves the caller's role *from the database on every request*
— deliberately not from the JWT's role claim, so a demotion takes effect immediately and a stale token
can never widen access. `AccountPermissions` is the single source of truth mapping
`AccountRole` → `AccountAction`:

| Action | Minimum role |
|---|---|
| `ReadResources` | Viewer |
| `ManageResources` | Member |
| `ManageMembers` | Admin |
| `ManageAccount` | Owner |

A missing `[RequireAccountAction]` is invisible: the endpoint simply works for everyone signed in, and
no happy-path test notices. `ShortLynx.Tests/Api/RoleEnforcementTests.cs` is where that gets pinned
down — extend it when you add a `/me/*` endpoint.

### The redirect hot path

`ShortLynx.Web` serves `/{code}` and is the only latency-critical path in the system:

```
rate limit (per IP) → cache lookup → 302 response
                                      └→ EnqueueAsync (Channel, non-blocking)
                                           └→ background IHostedService
                                                └→ IDbOperations bulk insert
```

The redirect returns before anything is written. Analytics are enqueued to an in-memory `Channel` and
drained in batches by a hosted service. **Never add a synchronous database write to this path.** If
you need a new signal recorded per click, add it to `VisitEvent` and let the sink persist it.

### Provider-specific queries

EF Core's SQLite provider cannot compare `DateTimeOffset` in SQL *at all*. Where a query needs a date
range, the codebase branches explicitly — PostgreSQL pushes the comparison into SQL, SQLite filters
client-side after narrowing by id:

```csharp
if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
    // resolve client-side
```

`VisitRetentionService.PruneOnceAsync` and `LinkVisitQueries.CountForAccountInRangeAsync` are the two
worked examples. If you hit a "could not be translated" error on a `DateTimeOffset` comparison, this
is why — check for existing precedent before rewriting the query shape.

### Quotas count live, never from a counter column

When gating on "how many of X does this account have", **query the owning table**. Do not read a
cached or denormalised counter unless something demonstrably increments it on every write. A counter
that looks maintained but isn't makes the check compare 0 against the cap and pass unconditionally —
a silent no-op no happy-path test catches. `UnlimitedEntitlements` sidesteps this by always allowing;
any real implementation must count.

### Fail fast on misconfiguration

Secrets are validated at startup and the app refuses to boot on a placeholder or too-short value
(`JwtOptions.Validate()`, `ApiKey:HmacSecret`, the Admin allowlist). This is intentional: a silently
insecure deployment is worse than one that won't start. Keep new secrets consistent with it, and cover
them in `ShortLynx.Tests/Api/StartupValidationTests.cs`.

### Conventions worth matching

- **IDs**: always `Guid.CreateVersion7()` (sequential — index-friendly). Never derive a short code
  from a primary key; codes are independent values.
- **Ownership**: everything scopes by `AccountId`. `UserAccountId`/`ApiKeyId` on an entity are
  *provenance* (who created it), not ownership. Filter by `AccountId`.
- **Comments explain *why*.** The codebase is unusually well-commented about rationale and rejected
  alternatives. Match that — a comment restating the code is noise; one explaining why the obvious
  approach was wrong saves the next person hours.

---

## How to add a feature

Worked example: a new account-scoped resource exposed on the API and the dashboard.

1. **Entity** → `ShortLynx.Data/Entities/`. Add the `DbSet` to `ShortLynxDbContext`; configure keys,
   indexes, and relationships in `ShortLynxDbContext.Configuration.cs`. Include `AccountId`.
2. **Migration** → generated into the provider project, never `Data`:
   ```bash
   dotnet ef migrations add YourChange \
     --project ShortLynx.Data.PostgreSql --startup-project ShortLynx.Core
   ```
   Verify it against a database at the *current production* migration level, not a freshly created
   one — a clean DB hides ordering and backfill problems.
3. **Service + interface** → `ShortLynx.Services/YourFeature/`. Take `accountId` as a parameter. No
   ASP.NET types. Throw domain exceptions (`EntitlementException`, `ArgumentException`) rather than
   returning HTTP-shaped results.
4. **Register in all three composition roots** (`Core`, `Admin`, `Web` — skip any that genuinely
   doesn't need it). Use `TryAdd*` if it's a seam.
5. **Controller** → `ShortLynx.Core/Controllers/`. Derive from `SessionControllerBase` for `/me/*`,
   add `[RequireAccountAction(...)]`, and translate domain exceptions to status codes. For API-key
   surfaces, add `[RequireScope(...)]` and extend `Scopes` if a genuinely new capability.
6. **UI** → `ShortLynx.Admin/Components/Pages/`. Gate visibility on the same `AccountPermissions`
   helpers the API uses, so UI and API can't drift.
7. **Tests** — see below.
8. **Tailwind**, if you touched markup that changes which classes are used: regenerate **last**, as
   the final step before committing, with no `dotnet build`/`test` in between. See CLAUDE.md; CI fails
   the PR on any diff.

### Testing

780 tests, xUnit, in `ShortLynx.Tests`. Mirror the structure of what you're testing
(`Services/`, `Api/`, `Repository/`, `Admin/`, `Data/`).

- **Unit** — `TestDatabase.CreateAsync()` gives a shared in-memory SQLite database; `CreateContext()`
  hands out independent `DbContext` instances over the same connection, so you can assert with a
  different context than you wrote with. Build entities via `Infrastructure/EntityFactory`.
- **Integration** — `ApiFactory` (`WebApplicationFactory`) boots the real Core pipeline against
  in-memory SQLite, forcing the SQLite provider through env vars so local user-secrets can't leak in.
  `AdminFactory` does the same for Blazor. Override config per-test with
  `WithWebHostBuilder(...AddInMemoryCollection(...))` — that's how the rate-limit tests lower limits.
- **Stub external I/O** — `InMemoryEmailSender`, `InMemoryDnsResolver`, `FakeDbOperations`. Never
  reach the network in a test.

**A test that passes is not the same as behaviour that works.** Two failure modes this repo has
actually been bitten by, both worth guarding against:

- A quota test that seeds the same counter field the implementation reads will pass whether or not the
  quota does anything. Seed the *real* resource.
- An integration test that synthesises a request header proves your *parsing*, not your
  *configuration*. `ForwardedHeadersTests`'s original two cases injected a single-hop
  `X-Forwarded-For` and passed for a year while production — which sends two hops — was broken. The
  fix was not just changing `ForwardLimit`; it was making the test model the real shape, including
  that the edge's own entry **rotates** per connection. A constant second entry would have passed
  under both the correct and the broken setting, proving nothing. When a test supplies the input the
  environment is supposed to supply, ask what it would take for the test to pass while production
  fails.

When fixing a bug, verify the new test **fails against the unfixed code**. Otherwise you've written a
test for the fix, not for the bug.

---

## Known traps

- **`ForwardLimit` must match the edge's real hop count.** *Fixed 2026-07-25 — recorded here because
  the failure mode is silent and easy to reintroduce.* All three apps trusted exactly one forwarded
  hop, but the production edge sends two (`<client>, <edge>`), so `RemoteIpAddress` resolved to the
  rotating internal edge address rather than the client. Per-IP rate limiting therefore never
  partitioned real clients together (nothing was ever throttled), and in `Web` the same value feeds
  `RawIp` → `HashedIp` *and* GeoIP country resolution, so visit analytics attributed clicks to the
  infrastructure rather than the visitor. Now `2`, overridable via `ForwardedHeaders:ForwardLimit`, and
  covered by tests that model a *rotating* edge hop. If a proxy is ever added in front (e.g.
  Cloudflare on a custom domain), this must increase — and note the analytics consequence, not just
  the rate-limiting one.
- **A literal `bin\Debug` directory** (one folder whose *name* contains a backslash) is occasionally
  created by MSBuild's BuildHost on macOS. It is gitignored via `**/bin\\Debug/`, but if it ever gets
  committed it breaks clean CI and Docker builds. Check `git status` before committing.
- **Tailwind CLI version must match CI** (`v4.3.2`, mirrored in each `.csproj`'s `TailwindVersion`).
  Different versions format identical input differently, so a mismatch fails CI's staleness check with
  a confusing pure-formatting diff. Always run the CLI from *inside* the project directory.
- **`docs/` is gitignored wholesale** (`docs/*`). Files already tracked there stay tracked, but a
  *new* doc placed in `docs/` will be silently invisible. Put public docs at the repo root, or
  `git add -f` deliberately.

---

## Pull requests

- Branch from `implementation` (the integration branch), not `main`.
- `dotnet build ShortLynx.slnx` and `dotnet test ShortLynx.slnx` green before pushing.
- CI runs build + all tests + the Tailwind staleness check. It must be green before merge.
- Keep commit messages explanatory: what changed, and *why* the obvious alternative wasn't chosen.
  The history is used as documentation here.
- Licence: ELv2 (`LICENSE`). Contributions are under the same terms. Nothing about hosted-service
  billing, pricing, or payment integration belongs in this repo — the `IEntitlements` seam is the
  entire public surface of that boundary.
