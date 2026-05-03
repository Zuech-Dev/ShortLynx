# ShortLynx — Implementation Plan

## Phases

| # | File | Summary |
|---|---|---|
| 1 | [01-open-decisions.md](01-open-decisions.md) | Resolve outstanding design questions before writing code |
| 2 | [02-data-layer.md](02-data-layer.md) | EF Core entities, DbContext, migrations, `IDbOperations` |
| 3 | [03-services.md](03-services.md) | Short code generation, visit pipeline, cache, URL validation, API keys |
| 4 | [04-redirect-pipeline.md](04-redirect-pipeline.md) | `GET /{code}` endpoint, rate limiting, visit recording background service |
| 5 | [05-link-management-api.md](05-link-management-api.md) | Link CRUD, Mode 2 bulk code generation, analytics query endpoints |
| 6 | [06-admin-ui.md](06-admin-ui.md) | Blazor Server admin dashboard |
| 7 | [07-web-public.md](07-web-public.md) | ShortLynx.Web Razor Pages public site |
| 8 | [08-operations.md](08-operations.md) | Docker, health checks, data retention, configuration reference |

## Dependency order

Phases must be completed in order — each builds on the previous:

```
01-open-decisions
      ↓
02-data-layer
      ↓
03-services
      ↓
04-redirect-pipeline ──→ 05-link-management-api
                                  ↓
                     06-admin-ui / 07-web-public (parallel)
                                  ↓
                            08-operations
```

## Key conventions (decided in DESIGN.md)

- All PKs: `Guid.CreateVersion7()`, configured with `ValueGeneratedNever()` in EF Core
- Short codes never derived from PKs
- Provider wiring via a single `AddShortLynxDatabase()` extension in `Program.cs`
- Separate migration assembly per database provider
- Redirect and visit recording are fully decoupled
