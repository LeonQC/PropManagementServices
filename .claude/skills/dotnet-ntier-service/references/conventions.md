# PropTrack N-tier conventions & per-aggregate slice

Read this before writing an aggregate's code. It covers the vertical-slice
pattern, the three-DTO mapping rules and why they exist, DI wiring, and the
database/seed conventions. The canonical implementation is
`src/ListingsService/` — cross-check against it.

## Table of contents
1. The three DTO families (and why)
2. Vertical slice, layer by layer (annotated)
3. Dependency injection wiring
4. Database, snake_case, migrations & seed
5. Gotchas we actually hit

---

## 1. The three DTO families (and why)

Data crosses two boundaries on its way in and out, and each boundary has its own
type family so the three shapes can evolve independently:

```
JSON ── HTTP DTO ──(controller maps)── business DTO ──(service maps)── entity ── DB
        (Api/DTOs)                      (Business/DTOs)                 (Models)
```

| Family       | Lives in        | Examples                                   | Owns / free to change |
|--------------|-----------------|--------------------------------------------|-----------------------|
| HTTP DTO     | `Api/DTOs`      | `CreatePropertyRequest`, `PropertyResponse`| the wire contract     |
| Business DTO | `Business/DTOs` | `CreatePropertyDto`, `PropertyDto`         | the use-case shape    |
| Entity       | `Models`        | `Property`, `Address`                       | the DB shape          |

Why three and not one: a change to the DB schema stops at the service's mapping;
a change to the API contract stops at the controller's mapping. The Api project
sets `DisableTransitiveProjectReferences` so it *cannot* see entities — if a
controller needs an entity type, a mapping is missing.

The cost is real: adding one field that flows end-to-end touches 3 types + 2
mappings. That's the deliberate tax for the decoupling. Don't collapse the
families to "save typing".

**Naming:** business DTOs use the `Dto` suffix with a direction prefix —
`CreateXDto` / `UpdateXDto` going in, `XDto` coming out. HTTP DTOs use the
`Request`/`Response` suffix. Keep `Request`/`Response` out of the Business layer
(it's taken by Api) and keep `Model` out of DTO names (it's the entity project).

## 2. Vertical slice, layer by layer (annotated)

Per aggregate `{{Aggregate}}`, create these. Snippets show the shape; mirror the
listings-service for the full version.

### Models/{{Aggregate}}.cs — plain entity
```csharp
namespace {{Svc}}Service.Models;

public class {{Aggregate}}
{
    public required string Id { get; set; }
    public required string Status { get; set; }
    // ...domain fields...
    public string? UpdatedAt { get; set; }
    // navigation properties for related entities
}
```
No EF attributes — all mapping is Fluent API in the DbContext. Entities are POCOs.

### DataAccess/I{{Aggregate}}Repository.cs + {{Aggregate}}Repository.cs
The interface lives **with** its implementation (plain N-tier; no cross-layer
dependency inversion). The repository takes the DbContext via constructor.
```csharp
public interface I{{Aggregate}}Repository
{
    Task<{{Aggregate}}?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<(List<{{Aggregate}}> Items, int TotalCount)> GetAllAsync(int page, int pageSize, /* filters */ CancellationToken ct = default);
    Task<{{Aggregate}}> CreateAsync({{Aggregate}} entity, CancellationToken ct = default);
    Task UpdateAsync({{Aggregate}} entity, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);   // soft delete
}

public class {{Aggregate}}Repository({{Svc}}DbContext db) : I{{Aggregate}}Repository
{
    public Task<{{Aggregate}}?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.{{Aggregate}}s.Include(/* related */).FirstOrDefaultAsync(x => x.Id == id, ct);
    // GetAllAsync: build the query, apply optional filters with chained .Where(),
    //   then CountAsync + Skip/Take for pagination.
    // DeleteAsync: ExecuteUpdateAsync to set Status to the soft-deleted value
    //   instead of removing the row.
}
```
Generate IDs (`Guid.NewGuid().ToString()`) in `CreateAsync`. List queries filter
out the soft-deleted status.

### Business/DTOs/{{Aggregate}}Dtos.cs — `record` types
`CreateXDto` (all required fields), `UpdateXDto` (all nullable — partial update),
and `XDto` (output). Plus child DTOs (`CreateAddressDto`, `AddressDto`, …).

### Business/{{Aggregate}}Service.cs — the business layer
Public surface speaks **only** business DTOs; entities are an internal detail.
Holds: validation, defaults, slug/timestamp generation, the partial-update merge,
entity↔DTO mapping, and event hooks (Kafka TODOs). Returns `null`/`false` to
signal "not found" so the controller can decide the HTTP status.
```csharp
public class {{Aggregate}}Service(I{{Aggregate}}Repository repo)
{
    public async Task<{{Aggregate}}Dto> CreateAsync(Create{{Aggregate}}Dto input, CancellationToken ct = default)
    {
        var entity = new {{Aggregate}} { Id = "", /* map fields */, UpdatedAt = Now() };
        var created = await repo.CreateAsync(entity, ct);
        // TODO: publish {{aggregate}}.created event
        return MapToDto(created);
    }

    public async Task<{{Aggregate}}Dto?> UpdateAsync(string id, Update{{Aggregate}}Dto input, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity is null) return null;
        if (input.SomeField is not null) entity.SomeField = input.SomeField;   // merge only provided fields
        await repo.UpdateAsync(entity, ct);
        return MapToDto(entity);
    }

    private static {{Aggregate}}Dto MapToDto({{Aggregate}} e) => new(/* ...fields... */);
}
```

### Api/DTOs/Requests.cs + Responses.cs — HTTP records
`Create{{Aggregate}}Request`, `Update{{Aggregate}}Request`, `{{Aggregate}}Response`,
plus presentation-only shapes the service doesn't know about (e.g. `GeoJson*`,
`PaginatedResponse<T>`).

### Api/Controllers/{{Aggregate}}sController.cs — thin
HTTP only. Bind the request, map HTTP DTO → business DTO, call one service method,
map the result back, translate `null`/`false` → `NotFound()`. **No entity types.**
```csharp
[ApiController]
[Route("{{svc}}/v1/{{aggregate}}s")]
public class {{Aggregate}}sController({{Aggregate}}Service service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<{{Aggregate}}Response>> Get(string id, CancellationToken ct)
    {
        var dto = await service.GetByIdAsync(id, ct);
        return dto is null ? NotFound() : Ok(MapToResponse(dto));
    }
    // POST maps Create…Request → Create…Dto; PUT maps Update…Request → Update…Dto.
    private static {{Aggregate}}Response MapToResponse({{Aggregate}}Dto d) => new(/* ...fields... */);
}
```

Route convention: `{{svc}}/v1/{{aggregate}}s` (plural), e.g.
`listings/v1/properties`. Controller class is plural (`PropertiesController`);
services and repositories are singular (`PropertyService`, `PropertyRepository`).

## 3. Dependency injection wiring

DI chains strictly downward so the Api only knows about Business:

- `Program.cs` → `builder.Services.AddBusiness(connStr)` (nothing else from the stack)
- `AddBusiness` → calls `AddDataAccess(connStr)`, then `AddScoped<{{Aggregate}}Service>()`
- `AddDataAccess` → `AddDbContext<{{Svc}}DbContext>(...UseSnakeCaseNamingConvention())`
  and `AddScoped<I{{Aggregate}}Repository, {{Aggregate}}Repository>()`

Services and repositories are **scoped** (one per HTTP request), which matches the
DbContext lifetime. The service is registered as its concrete type (no
`IXxxService` interface — there's only ever one implementation, and an interface
in the same layer as its impl earns nothing here).

## 4. Database, snake_case, migrations & seed

- **snake_case**: `UseSnakeCaseNamingConvention()` maps `PropertyType` →
  `property_type` automatically. Only configure what convention can't infer:
  relationships, indexes, and digit-name overrides.
- **Schema + seed live in `db/init/`** at the repo root, named to run in order
  (`01-schema.sql`, `02-seed-data.sql`). Postgres auto-runs everything in
  `/docker-entrypoint-initdb.d/` **only on first volume creation**.
- **Re-seeding**: because init scripts run only on a fresh volume,
  `docker-compose down -v` (the `-v` wipes the volume) then `up` is how you apply
  schema/seed changes during development. A plain `down` keeps the data.
- **Booleans**: use Postgres `BOOLEAN` and a C# `bool`. Don't store 0/1 ints — that
  leaks a storage detail into the entity (and forces `== 1` conversions).
- This setup uses raw SQL init scripts rather than EF migrations. If a service
  prefers EF migrations instead, generate them with the EF tools and apply on
  startup — but keep the `db/init` approach unless the user asks otherwise, for
  consistency with listings-service.

## 5. Gotchas we actually hit

- **Digit-in-name columns**: `Year1NoiEstimate` → convention yields
  `year1noi_estimate`, not `year1_noi_estimate`. Override with `.HasColumnName(...)`.
- **EF Core package version skew**: the Web SDK pulls an older EF Core than
  `EFCore.NamingConventions`/Npgsql want, causing a runtime
  `FileNotFoundException: Microsoft.EntityFrameworkCore.Relational 9.0.5`. The
  template pins `Microsoft.EntityFrameworkCore.Relational 9.0.5` explicitly in
  DataAccess to keep versions aligned — keep that pin.
- **`http` not `https` locally**: the API listens on plain HTTP in the container
  (port 8080 → host port). `curl https://localhost:{{ApiHostPort}}` fails with a
  TLS error; use `http://`.
- **Init scripts didn't run**: almost always a stale volume. `down -v` then `up`.
- **`Microsoft.AspNetCore.Mvc` using**: needed in controllers (for
  `ControllerBase`, `[ApiController]`, `ActionResult<T>`) but the Web SDK does not
  add it to implicit usings — import it explicitly in each controller.
