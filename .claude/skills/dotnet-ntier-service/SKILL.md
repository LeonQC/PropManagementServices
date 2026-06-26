---
name: dotnet-ntier-service
description: >-
  Scaffold a new ASP.NET Core microservice that follows the PropTrack N-tier
  architecture (Api → Business → DataAccess → Models, with EF Core + PostgreSQL
  and Docker Compose). Use this whenever creating, scaffolding, or adding a new
  backend service to the PropTrack monorepo — e.g. deals-service, ai-service,
  notifications-service — or when a user asks to "set up a new service", "add a
  service following our pattern", "scaffold the X service", or wire a new
  controller/repository into an existing PropTrack service. Also use it to check
  that an existing service matches the house conventions (layer references, DTO
  boundaries, DI chaining). Prefer this skill over scaffolding a .NET service
  from scratch so every service in the monorepo stays consistent.
---

# PropTrack N-tier Service Scaffold

This skill encodes the house architecture for PropTrack backend services so every
service looks the same. It is classic **N-tier** (not Clean Architecture): each
layer references **only the one directly beneath it**, and control flows straight
down with no inverted dependencies.

```
Api          (controllers + HTTP DTOs)          ── references ▶ Business
Business     (services + business DTOs)         ── references ▶ DataAccess
DataAccess   (DbContext, repository + interface) ── references ▶ Models
Models       (EF-mapped entities)               ── references ▶ (nothing)
                                                                  │
                                                            PostgreSQL
```

The reference book example is the **listings-service** in this same repo
(`src/ListingsService/`). When in doubt, read that code — it is the canonical
implementation of everything described here.

## Placeholder legend

Templates in `assets/templates/` use these tokens. Replace every occurrence when
you copy a template. **Pick concrete values with the user before scaffolding.**

| Token            | Meaning                                  | Example (listings) | Example (deals) |
|------------------|------------------------------------------|--------------------|-----------------|
| `{{Svc}}`        | Service noun, PascalCase                  | `Listings`         | `Deals`         |
| `{{svc}}`        | Service noun, lowercase                   | `listings`         | `deals`         |
| `{{project}}`    | Product prefix for DB name/credentials    | `proptrack`        | `proptrack`     |
| `{{ApiHostPort}}`| Host port mapped to the API container     | `5100`             | `5200`          |
| `{{DbHostPort}}` | Host port mapped to Postgres              | `5432`             | `5433`          |
| `{{Aggregate}}`  | Primary entity / aggregate root, singular | `Property`         | `Deal`          |
| `{{aggregate}}`  | Primary entity, lowercase                 | `property`         | `deal`          |

Projects are named `{{Svc}}Service.<Layer>` and share namespace roots with their
folders (e.g. `DealsService.Business.DTOs`). The DbContext is `{{Svc}}DbContext`;
the connection-string key is `{{Svc}}Db`.

## Scaffolding procedure

Work from the repo root. The new service lives at `src/{{Svc}}Service/`.

1. **Confirm the inputs** with the user: service noun, primary aggregate(s), and
   the two host ports (each service needs unique ports — check existing
   `docker-compose.yml` files so they don't collide).

2. **Create the four projects and solution.** Prefer `dotnet new` so the SDK
   wiring is correct, then overwrite the generated `.csproj` files with the
   templates (the templates carry the exact package versions and the strict
   reference chain):
   ```bash
   cd src/{{Svc}}Service
   dotnet new sln -n {{Svc}}Service
   dotnet new classlib -n {{Svc}}Service.Models     -o {{Svc}}Service.Models     -f net9.0
   dotnet new classlib -n {{Svc}}Service.DataAccess -o {{Svc}}Service.DataAccess -f net9.0
   dotnet new classlib -n {{Svc}}Service.Business    -o {{Svc}}Service.Business    -f net9.0
   dotnet new webapi   -n {{Svc}}Service.Api         -o {{Svc}}Service.Api         -f net9.0 --no-openapi
   dotnet sln add {{Svc}}Service.Models {{Svc}}Service.DataAccess {{Svc}}Service.Business {{Svc}}Service.Api
   rm {{Svc}}Service.Models/Class1.cs {{Svc}}Service.DataAccess/Class1.cs {{Svc}}Service.Business/Class1.cs
   ```
   Then copy each `assets/templates/*.csproj` over the generated one, substituting
   placeholders. The reference chain (Api→Business→DataAccess→Models) is baked
   into the templates — do not add reference shortcuts that skip a layer.

3. **Lay down the cross-cutting boilerplate** from `assets/templates/`:
   - `Program.cs` → `{{Svc}}Service.Api/Program.cs`
   - `AddBusiness.cs` → `{{Svc}}Service.Business/ServiceCollectionExtensions.cs`
   - `AddDataAccess.cs` → `{{Svc}}Service.DataAccess/ServiceCollectionExtensions.cs`
   - `DbContext.cs` → `{{Svc}}Service.DataAccess/{{Svc}}DbContext.cs`
   - `appsettings.json` → `{{Svc}}Service.Api/appsettings.json` (merge with generated one)
   - `Dockerfile` → `{{Svc}}Service.Api/Dockerfile`
   - `docker-compose.yml` → repo root (or merge a new pair of services into the
     existing compose file if the monorepo already has one)

4. **Build the per-aggregate vertical slice.** For each aggregate, create the six
   pieces that span the layers. Read `references/conventions.md` for the annotated
   pattern and the DTO/mapping rules — it is the most important reference here.
   Briefly, per aggregate you add:
   - `Models/{{Aggregate}}.cs` — the entity (plain POCO, EF-mapped in DbContext)
   - `DataAccess/I{{Aggregate}}Repository.cs` + `{{Aggregate}}Repository.cs`
   - `Business/DTOs/{{Aggregate}}Dtos.cs` — `Create…Dto`, `Update…Dto`, `…Dto`
   - `Business/{{Aggregate}}Service.cs` — maps DTO ↔ entity, holds business logic
   - `Api/DTOs/Requests.cs` + `Responses.cs` — HTTP request/response records
   - `Api/Controllers/{{Aggregate}}sController.cs` — thin; maps HTTP DTO ↔ business DTO

   Register the new service + repository in the two `ServiceCollectionExtensions`
   files (uncomment / add the `AddScoped` lines).

5. **Seed/migrate the database.** Put schema + seed SQL in `db/init/` (named
   `01-schema.sql`, `02-seed-data.sql`, …). Postgres runs these automatically on
   first volume creation. See `references/conventions.md` → "Database & migrations".

6. **Build and verify.**
   ```bash
   dotnet build
   docker-compose up --build -d   # then hit http://localhost:{{ApiHostPort}}/swagger
   ```

## The rules that make this pattern work

These are the load-bearing conventions. Don't quietly drop them — they're why the
services stay consistent and decoupled.

- **One layer down only.** Every `.csproj` references exactly the layer beneath it.
  The Api project additionally sets `<DisableTransitiveProjectReferences>true</…>`
  so it physically *cannot* see `DataAccess`, `Models`, or EF Core. If a controller
  ever needs an entity type, that's the signal a mapping is missing — fix the
  mapping, don't add a reference.

- **Three DTO families, mapped at each boundary.** HTTP DTOs (Api) ↔ business DTOs
  (Business) ↔ entities (Models). The controller maps HTTP↔business; the service
  maps business↔entity. This keeps the wire contract, the use-case shape, and the
  DB shape free to evolve independently. Details and rationale in
  `references/conventions.md`.

- **DI chains downward.** `Program.cs` calls only `AddBusiness(connStr)`;
  `AddBusiness` calls `AddDataAccess`. The Api never registers a DbContext or
  repository directly — it doesn't know they exist.

- **Repository interface lives with its implementation** in DataAccess (plain
  N-tier — no dependency inversion across layers here).

- **Thin controllers.** Controllers do HTTP only: bind the request, call one
  service method, translate the result to a status code. Services return
  `null`/`false` to signal "not found"; the controller turns that into `404`.

- **snake_case via convention.** `UseSnakeCaseNamingConvention()` maps PascalCase
  properties to snake_case columns automatically. Watch the digit gotcha:
  `Year1NoiEstimate` → `year1noi_estimate`, which may not match the real column —
  override with `.HasColumnName(...)` when needed.

- **Soft delete.** `DELETE` sets a status column (e.g. `off_market`) rather than
  removing the row; list queries filter the soft-deleted state out.

## Reference files

- `references/conventions.md` — the per-aggregate vertical-slice pattern (annotated
  code), the full DTO/mapping rationale, DI wiring, database/seed conventions, and
  the common gotchas we actually hit. **Read this before writing the slice.**
- `assets/templates/` — copy-and-substitute boilerplate (csproj ×4, Program.cs,
  the two DI extension files, DbContext skeleton, appsettings, Dockerfile,
  docker-compose).
