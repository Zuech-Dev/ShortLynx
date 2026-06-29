# Phase 2 — Data Layer

Prerequisites: Phase 1 decisions recorded, particularly Q1–Q3 (table layout) and Q7 (min .NET version).

---

## Step 1: Create `ShortLynx.Data` project

This shared project holds entities and `DbContext`. No provider-specific code here.

```bash
dotnet new classlib -n ShortLynx.Data -f net10.0
dotnet sln ShortLynx.slnx add ShortLynx.Data/ShortLynx.Data.csproj
```

Add NuGet packages:
- `Microsoft.EntityFrameworkCore` (no provider — keeps this project provider-agnostic)

---

## Step 2: Define entities in `ShortLynx.Data`

### `Link`
```
Link
├── Id: Guid (PK, ValueGeneratedNever)
├── OriginalUrl: string (required)
├── CreatedAt: DateTimeOffset
├── ExpiresAt: DateTimeOffset? (nullable = no expiry)
├── IsActive: bool
├── ApiKeyId: Guid (FK → ApiKey)
└── Mode: LinkMode (enum: Anonymous = 1, UserAttributed = 2)
```

### `UserLinkCode` (Mode 2 only)
```
UserLinkCode
├── Id: Guid (PK, ValueGeneratedNever)
├── LinkId: Guid (FK → Link)
├── UserId: Guid (external user identity — no FK, not managed by ShortLynx)
├── Code: string (unique index)
├── CreatedAt: DateTimeOffset
├── IsActive: bool
├── IsOneTimeUse: bool
└── IsUsed: bool
```

Unique constraint: `(LinkId, UserId)` — DB-enforced idempotency for Mode 2 code generation.

### `ShortCode` (Mode 1 only)
```
ShortCode
├── Id: Guid (PK, ValueGeneratedNever)
├── LinkId: Guid (FK → Link, unique)
├── Code: string (unique index)
└── CreatedAt: DateTimeOffset
```

### `Visit` (Mode 1 aggregate)
```
Visit
├── Id: Guid (PK, ValueGeneratedNever)
├── ShortCodeId: Guid (FK → ShortCode)
├── ClickedAt: DateTimeOffset
├── HashedIp: string
├── Referrer: string?
└── UserAgent: string?
```

PostgreSQL optimization: mark `visits` as `UNLOGGED TABLE` in the migration.

### `UserVisit` (Mode 2 attributed)
```
UserVisit
├── Id: Guid (PK, ValueGeneratedNever)
├── UserLinkCodeId: Guid (FK → UserLinkCode)
├── UserId: Guid (denormalized from UserLinkCode — see Q3)
├── ClickedAt: DateTimeOffset
├── HashedIp: string
├── Referrer: string?
└── UserAgent: string?
```

### `ApiKey`
```
ApiKey
├── Id: Guid (PK, ValueGeneratedNever)
├── Prefix: string (first 8 chars of plaintext key, for lookup)
├── KeyHash: string (HMAC-SHA256 or Argon2 hash of plaintext key)
├── Name: string (human label)
├── CreatedAt: DateTimeOffset
├── ExpiresAt: DateTimeOffset?
├── IsActive: bool
└── Scopes: string (comma-delimited or JSON array of allowed operations)
```

---

## Step 3: Define `ShortLynxDbContext`

- Inherits `DbContext`
- DbSets for all entities above
- `OnModelCreating`: configure all keys, indexes, and constraints using EF Fluent API
  - All GUID PKs: `.ValueGeneratedNever()`
  - `ShortCode.Code`: `.HasIndex(x => x.Code).IsUnique()`
  - `UserLinkCode.Code`: `.HasIndex(x => x.Code).IsUnique()`
  - `UserLinkCode (LinkId, UserId)`: `.HasIndex(x => new { x.LinkId, x.UserId }).IsUnique()`
  - `ApiKey.Prefix`: `.HasIndex(x => x.Prefix)`
  - PostgreSQL-specific: partial index on `ShortCode.Code WHERE IsActive = true` (added in the Postgres migration, not here)

---

## Step 4: Define `IDbOperations`

In `ShortLynx.Data` (or `ShortLynx.Repository`):

```csharp
public interface IDbOperations
{
    Task BulkInsertUserLinkCodesAsync(
        IEnumerable<UserLinkCode> codes, CancellationToken ct = default);

    Task BulkInsertVisitsAsync(
        IEnumerable<Visit> visits, CancellationToken ct = default);

    Task BulkInsertUserVisitsAsync(
        IEnumerable<UserVisit> visits, CancellationToken ct = default);
}
```

Default implementation (`EfCoreDbOperations`) uses `EFCore.BulkExtensions`. This lives in `ShortLynx.Repository`.

PostgreSQL override (`PostgresDbOperations`) uses `COPY` binary import or `ON CONFLICT DO NOTHING`. Registered conditionally in the PostgreSQL provider registration.

---

## Step 5: Create migration projects

### PostgreSQL
```bash
dotnet new classlib -n ShortLynx.Data.PostgreSQL -f net10.0
dotnet sln ShortLynx.slnx add ShortLynx.Data.PostgreSQL/ShortLynx.Data.PostgreSQL.csproj
```

Packages:
- `Microsoft.EntityFrameworkCore.Design`
- `Npgsql.EntityFrameworkCore.PostgreSQL`

Reference `ShortLynx.Data`.

Add a `DesignTimeDbContextFactory` that reads `DATABASE_URL` or a dev `appsettings.Development.json`.

Run initial migration:
```bash
dotnet ef migrations add InitialCreate \
  --project ShortLynx.Data.PostgreSQL \
  --startup-project ShortLynx.Core
```

After `InitialCreate` is generated, manually edit it to:
- Set `visits` table to `UNLOGGED` via raw SQL in `migrationBuilder.Sql(...)`
- Add partial index: `CREATE INDEX ix_short_codes_active ON short_codes (code) WHERE is_active = true;`

### SQLite
```bash
dotnet new classlib -n ShortLynx.Data.Sqlite -f net10.0
dotnet sln ShortLynx.slnx add ShortLynx.Data.Sqlite/ShortLynx.Data.Sqlite.csproj
```

Packages:
- `Microsoft.EntityFrameworkCore.Design`
- `Microsoft.EntityFrameworkCore.Sqlite`

Reference `ShortLynx.Data`. No provider-specific optimizations needed.

---

## Step 6: Wire provider registration

In `ShortLynx.Core/Extensions/DatabaseExtensions.cs` (or similar):

```csharp
public static IServiceCollection AddShortLynxDatabase(
    this IServiceCollection services, IConfiguration configuration)
{
    var provider = configuration["Database:Provider"]
        ?? throw new InvalidOperationException("Database:Provider is required.");
    var connectionString = configuration["Database:ConnectionString"]
        ?? throw new InvalidOperationException("Database:ConnectionString is required.");

    switch (provider.ToLowerInvariant())
    {
        case "postgresql":
            services.AddDbContext<ShortLynxDbContext>(o =>
                o.UseNpgsql(connectionString,
                    x => x.MigrationsAssembly("ShortLynx.Data.PostgreSQL")));
            services.AddScoped<IDbOperations, PostgresDbOperations>();
            break;
        case "sqlite":
            services.AddDbContext<ShortLynxDbContext>(o =>
                o.UseSqlite(connectionString,
                    x => x.MigrationsAssembly("ShortLynx.Data.Sqlite")));
            services.AddScoped<IDbOperations, EfCoreDbOperations>();
            break;
        default:
            throw new InvalidOperationException($"Unsupported provider: {provider}");
    }

    return services;
}
```

Call `builder.Services.AddShortLynxDatabase(builder.Configuration)` in `Program.cs` of each host project.

---

## Step 7: Repository layer in `ShortLynx.Repository`

Define repository interfaces in `ShortLynx.Models` (or a `ShortLynx.Abstractions` project):
- `ILinkRepository`: CRUD for `Link`
- `IShortCodeRepository`: lookup by code, create, deactivate
- `IUserLinkCodeRepository`: lookup by code, bulk create, deactivate
- `IApiKeyRepository`: lookup by prefix, validate hash
- `IVisitRepository` / `IUserVisitRepository`: write-only for the visit pipeline

Implement each interface in `ShortLynx.Repository` using `ShortLynxDbContext` directly (no Unit of Work overhead needed).

---

## Verification

1. `dotnet build ShortLynx.slnx` — no errors
2. `dotnet ef migrations list --project ShortLynx.Data.PostgreSQL --startup-project ShortLynx.Core` — lists `InitialCreate`
3. Against a local Postgres instance: `dotnet ef database update --project ShortLynx.Data.PostgreSQL --startup-project ShortLynx.Core` — schema created with all tables, indexes, and constraints
4. Confirm `visits` table is `UNLOGGED` via `\d+ visits` in `psql`

Next: [Phase 3 — Services](03-services.md)
