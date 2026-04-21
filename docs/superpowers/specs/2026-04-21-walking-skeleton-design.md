# Walking Skeleton Design Spec (Phase 0, Slice 1)

**Date:** 2026-04-21
**Status:** Approved
**Scope:** End-to-end vertical slice of the Kartova stack (modular monolith + Wolverine + migrator + docker-compose + frontend shell + CI + minimal Helm) that wires every structural decision without implementing any domain feature.

## Problem

Kartova's architecture depends on 88 accepted ADRs that must integrate correctly. If we begin building domain features before the stack is proven wired end-to-end, every feature slice risks discovering an integration defect late (wrong migrator pattern, broken module boundary enforcement, mis-configured Wolverine, Tailwind tokens not loading, CI not gating). A solo developer cannot afford to debug six unrelated integration issues at once.

Slice 1 is therefore **not a feature** — it is the minimum vertical slice that proves every load-bearing structural ADR works together. Each subsequent slice extends a running skeleton rather than debugging foundational wiring.

Specifically, Slice 1 must prove:

- Modular monolith structure per ADR-0082 (one `Catalog` module as canary)
- NetArchTest fitness functions run as CI gate from commit 1 (ADR-0083)
- Migrator container pattern per ADR-0085 (runs a real no-op migration)
- Docker Compose local dev per ADR-0024 (postgres + migrator + api wire up)
- Wolverine registration with PostgreSQL persistence (ADR-0080) — no handlers yet, but the scaffolding is live
- REST API with health probes per ADR-0060 and a version endpoint
- Frontend build chain per ADR-0039 / ADR-0088 (Vite + TS strict + Tailwind + shadcn/ui)
- DESIGN.md tokens flow into Tailwind config and render in shadcn components
- Helm chart skeleton per ADR-0086 (`helm lint` clean, full templates in Slice 4)
- GitHub Actions CI builds and runs architecture + unit + integration tests

## Decisions

| Topic | Decision |
|-------|----------|
| Frontend scope | Minimal shell — React Router, `Sidebar` + `TopBar` per DESIGN.md, placeholder route `/catalog` ("Coming in Slice 3"), base shadcn components installed (`Button`, `Card`, `Sidebar`) |
| Migrator scope | No-op migration creating `__kartova_metadata` table with one row `('catalog', 1, NOW())` — proves pipeline end-to-end |
| First domain endpoint | `GET /api/v1/version` returning `{version, commit, buildTime}` from assembly metadata |
| Contracts naming | Per-module: `Kartova.{Module}.Contracts` (aligned with ADR-0082 boundary rules) |
| Test layout | **Co-located per module**: `src/Modules/Catalog/Kartova.Catalog.Tests/` + `Kartova.Catalog.IntegrationTests/`; only cross-cutting tests in `tests/` (`Kartova.ArchitectureTests/`, placeholder `Kartova.E2E/`, `Kartova.ContractTests/`) — ADR-0083 Implementation Notes to be updated post-Slice-1 to reflect this |
| Container base image | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` for API and migrator |
| .NET version pin | `global.json` with `"version": "10.0.100", "rollForward": "latestFeature"` |
| Local dev commands | `Makefile` in root with targets `up`, `down`, `rebuild`, `test`, `archtest`, `web`, `logs` |
| Solution file | Single `Kartova.sln` in repo root |
| .gitignore additions | `node_modules/`, `web/dist/`, `TestResults/`, `.vs/`, `*.DotSettings.user`, `.idea/`, `.env.local`, `coverage/` |
| docker-compose services (Slice 1) | `postgres` + `migrator` + `api` only; KeyCloak → Slice 2, Kafka → Slice 3, Elasticsearch → later |
| Wolverine scope | Registered with PostgreSQL message persistence; zero handlers, zero Kafka routing (Slice 3) |

## Design

### Repo structure after Slice 1

```
Roman.Gig2/
├── CLAUDE.md                              # existing
├── global.json                            # .NET 10.0.100 pin, rollForward latestFeature
├── Kartova.sln
├── Makefile                               # up/down/rebuild/test/archtest/web/logs
├── .gitignore                             # existing + additions above
├── .editorconfig
├── .github/
│   └── workflows/
│       └── ci.yml                         # backend + frontend jobs
├── docker-compose.yml                     # postgres + migrator + api
├── config/
│   └── keycloak/                          # placeholder dir (content in Slice 2)
├── docs/                                  # existing
├── src/
│   ├── Kartova.SharedKernel/
│   │   ├── Kartova.SharedKernel.csproj
│   │   ├── TenantId.cs                    # record struct
│   │   └── DomainEvent.cs                 # abstract record, base event type
│   ├── Kartova.Api/
│   │   ├── Kartova.Api.csproj
│   │   ├── Program.cs                     # Wolverine + HealthChecks + /api/v1/version + module registration
│   │   ├── Dockerfile                     # aspnet:10.0-alpine
│   │   └── Modules/
│   │       └── IModule.cs                 # interface RegisterServices + ConfigureWolverine
│   ├── Kartova.Migrator/
│   │   ├── Kartova.Migrator.csproj
│   │   ├── Program.cs                     # iterates IModule[], resolves DbContext scope, Migrate()
│   │   └── Dockerfile                     # aspnet:10.0-alpine
│   └── Modules/
│       └── Catalog/
│           ├── Kartova.Catalog.Domain/
│           │   └── CatalogDomainMarker.cs # assembly anchor
│           ├── Kartova.Catalog.Application/
│           │   └── CatalogApplicationMarker.cs
│           ├── Kartova.Catalog.Infrastructure/
│           │   ├── CatalogDbContext.cs    # configures KartovaMetadata entity
│           │   ├── CatalogModule.cs       # implements IModule
│           │   └── Migrations/
│           │       └── 20260421_InitialCatalog.cs
│           ├── Kartova.Catalog.Contracts/
│           │   └── CatalogContractsMarker.cs
│           ├── Kartova.Catalog.Tests/
│           │   └── CatalogAssemblyLoadsTests.cs   # smoke test
│           └── Kartova.Catalog.IntegrationTests/
│               ├── Fixtures/PostgresFixture.cs
│               └── Migrations/MigrationIntegrationTests.cs
├── tests/                                  # cross-cutting only (in Slice 1: just ArchitectureTests)
│   └── Kartova.ArchitectureTests/
│       ├── CleanArchitectureLayerTests.cs
│       ├── ModuleBoundaryTests.cs
│       └── ForbiddenDependencyTests.cs
├── web/
│   ├── package.json                       # React 19 + TS strict + Vite + Tailwind v4 + shadcn
│   ├── tsconfig.json                      # strict
│   ├── vite.config.ts
│   ├── tailwind.config.ts                 # imports DESIGN.md tokens as CSS vars
│   ├── components.json                    # shadcn config
│   ├── Dockerfile                         # multi-stage: node:20-alpine build + nginx:alpine serve
│   ├── index.html
│   └── src/
│       ├── main.tsx
│       ├── App.tsx                        # React Router: / → /catalog (placeholder)
│       ├── components/
│       │   ├── ui/
│       │   │   ├── button.tsx             # shadcn
│       │   │   ├── card.tsx               # shadcn
│       │   │   └── sidebar.tsx            # shadcn
│       │   └── layout/
│       │       ├── Sidebar.tsx            # per DESIGN.md nav spec
│       │       └── TopBar.tsx             # 56px, logo + disabled search
│       └── lib/utils.ts                   # cn() helper
└── deploy/
    └── helm/
        └── kartova/
            ├── Chart.yaml                 # appVersion 0.1.0
            ├── values.yaml                # minimal defaults
            └── templates/
                ├── api-deployment.yaml    # skeleton; expanded in Slice 4
                ├── migrator-job.yaml      # ADR-0085 pre-install/pre-upgrade hook
                └── _helpers.tpl
```

### Components

**Backend (.NET 10):**

1. **`Kartova.SharedKernel`** — one `TenantId` record struct, one `DomainEvent` abstract record. No external dependencies.
2. **`Kartova.Api`** — composition root. `Program.cs` bootstraps Wolverine with PostgreSQL persistence (no Kafka routing, no handlers), registers ASP.NET Core HealthChecks (three probes per ADR-0060), maps `GET /api/v1/version`, and explicitly invokes `new CatalogModule().RegisterServices(builder.Services)`. No controllers; uses Minimal APIs for the two endpoints.
3. **`Kartova.Migrator`** — console app. `Program.cs` iterates over an `IModule[]` list (Slice 1: one entry), resolves each module's `DbContext` from a scope, calls `await ctx.Database.MigrateAsync()`, exits 0 on success. Read-only filesystem, non-root user.
4. **`Modules/Catalog`** — canary module:
   - `Domain` and `Application` are structurally present (one marker type each) but empty
   - `Infrastructure` has `CatalogDbContext` configuring a single `KartovaMetadata` entity: `(module_name text PK, schema_version int, applied_at timestamptz)`
   - `CatalogModule : IModule` registers `CatalogDbContext` with `UseNpgsql`
   - One initial migration `20260421_InitialCatalog` creates the table and inserts `('catalog', 1, NOW())`
   - `Contracts` is empty (placeholder marker type for cross-references)

**Frontend (Vite + React + TS strict):**

- `App.tsx` — React Router with two routes: `/` redirects to `/catalog`, `/catalog` renders the shell layout with a placeholder card "Coming in Slice 3"
- `components/layout/Sidebar.tsx` — 260px sidebar per DESIGN.md, nav items: `Catalog` (active), `Services` / `Infrastructure` / `Docs` / `Settings` (disabled via `data-disabled`)
- `components/layout/TopBar.tsx` — 56px top bar, "Kartova" logo + disabled search input placeholder
- `components/ui/{button,card,sidebar}.tsx` — shadcn base components, copied into repo (no package dependency)
- `tailwind.config.ts` — imports Slate palette and typography tokens from DESIGN.md as CSS variables
- No API calls, no data fetching

### Data flow — first `make up`

```
1. docker compose up -d postgres
   → healthcheck pg_isready passes (PostgreSQL 16-alpine)
2. docker compose up migrator
   → connects Host=postgres;Database=kartova;Username=migrator;Password=dev
   → resolves CatalogDbContext
   → Database.MigrateAsync() applies 20260421_InitialCatalog
   → inserts metadata row
   → exit 0
3. docker compose up api
   → UseWolverine() bootstraps, creates its own persistence tables if missing
   → MapHealthChecks: /health/live, /health/ready, /health/startup
   → MapGet /api/v1/version → assembly metadata
   → listens on :8080
4. curl http://localhost:8080/health/ready → 200 Healthy
5. curl http://localhost:8080/api/v1/version → 200 {"version":"0.1.0","commit":"<sha>","buildTime":"<iso>"}
6. (separate) cd web && npm run dev → http://localhost:5173
   → shell renders, /catalog placeholder visible, zero console errors
```

### Architecture tests (first three NetArchTest rules)

**`CleanArchitectureLayerTests.Domain_Does_Not_Reference_Infrastructure`** — verifies `Kartova.Catalog.Domain` has no dependency on `Microsoft.EntityFrameworkCore` or any `Kartova.*.Infrastructure` assembly.

**`ModuleBoundaryTests.Catalog_Does_Not_Reference_Other_Modules_Internals`** — verifies types in `Kartova.Catalog.*` do not reference any other module's `Domain`, `Application`, or `Infrastructure`. In Slice 1 the assertion is vacuously true (no other modules exist), but the test scaffolds the rule so adding a second module in Slice 2 requires only extending the assembly list.

**`ForbiddenDependencyTests.No_Module_References_MediatR_Or_MassTransit`** — verifies no assembly in the solution depends on `MediatR` or `MassTransit` (per ADR-0080).

All three tests target < 50 ms each; full tier < 1 second.

### CI pipeline (`.github/workflows/ci.yml`)

Two jobs, parallel:

- **backend**:
  1. `actions/setup-dotnet@v4` pinned by `global.json`
  2. `dotnet restore Kartova.sln`
  3. `dotnet build --configuration Release --no-restore`
  4. `dotnet test tests/Kartova.ArchitectureTests/ --no-build` (tier 1, fast-fail)
  5. `dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/ --no-build` (tier 2)
  6. `dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ --no-build` (tier 3, Testcontainers PostgreSQL)

- **frontend** (working-directory `web/`):
  1. `actions/setup-node@v4` (Node 20 LTS)
  2. `npm ci`
  3. `npm run typecheck`
  4. `npm run build`

Both jobs must pass on every push.

### Helm chart skeleton

`deploy/helm/kartova/`:
- `Chart.yaml` — `apiVersion: v2`, `name: kartova`, `version: 0.1.0`, `appVersion: "0.1.0"`
- `values.yaml` — image repo placeholders, minimal config
- `templates/api-deployment.yaml` — single-replica Deployment referencing `{{ .Values.image.repository }}/api:{{ .Values.image.tag }}`, env vars for connection string
- `templates/migrator-job.yaml` — Helm `pre-install,pre-upgrade` hook Job running the migrator image
- `templates/_helpers.tpl` — standard name/labels helpers

CI does not deploy the chart in Slice 1; only `helm lint` + `helm template | kubectl apply --dry-run=client` run as validation.

## Testing strategy

Three of the five ADR-0083 test tiers are active in Slice 1:

- **Tier 1 — Architecture tests** (mandatory CI gate): three rules as described above. Target < 1 s total; any violation fails CI before other tiers run.
- **Tier 2 — Unit tests**: one smoke test in `Kartova.Catalog.Tests` verifying the assembly loads; real unit testing begins in Slice 3 when domain logic exists.
- **Tier 3 — Integration tests**: `MigrationIntegrationTests` in `Kartova.Catalog.IntegrationTests` uses Testcontainers PostgreSQL to start a real database, run the migrator entry point, and assert that `__kartova_metadata` contains the expected row. A second test asserts migrator idempotency (running twice produces no duplicate rows or exceptions).

**Tier 4 (Contract tests)** and **Tier 5 (E2E / Playwright)** do not exist in Slice 1. The corresponding `tests/Kartova.ContractTests/` and `tests/Kartova.E2E/` projects are created in Slice 3 when the first real feature lands.

**Frontend verification**: no automated test framework yet. `npm run typecheck` + `npm run build` must succeed. Manual verification via Playwright MCP per ADR-0084: navigate `/`, verify redirect to `/catalog`, take snapshot, confirm no console errors. No authored Playwright E2E tests yet.

Total test suite runtime target: < 30 seconds locally (`make test`).

## Success criteria

Slice 1 is complete when all of the following hold:

### Infrastructure
- `make up` starts all services; none in restart loop
- `docker compose logs migrator` shows the migration being applied and exit code 0
- `psql ... -c "SELECT * FROM __kartova_metadata"` returns one row `('catalog', 1, <timestamp>)`
- `make down` removes all containers and volumes cleanly

### Backend
- `GET /health/live` → 200 `{"status":"Healthy"}`
- `GET /health/ready` → 200 after migrator completes; 503 before (validates dependency ordering)
- `GET /health/startup` → 200 after startup
- `GET /api/v1/version` → 200 `{"version":"0.1.0","commit":"<git sha>","buildTime":"<ISO 8601>"}`

### Frontend
- `cd web && npm run dev` serves http://localhost:5173
- `/` redirects to `/catalog`; placeholder "Coming in Slice 3" renders
- `Sidebar` and `TopBar` render per DESIGN.md tokens (dark Slate, active `Catalog`, disabled rest)
- Zero console errors in DevTools
- `npm run build` succeeds; `npm run typecheck` reports zero errors

### Tests
- `make archtest` — three tests green
- `make test` — full suite green including Testcontainers integration test
- Local `make test` wall clock < 30 seconds

### CI
- `ci.yml` triggers on push to master
- Both jobs (backend, frontend) pass
- No individual step exceeds 3 minutes wall clock

### Helm
- `helm lint deploy/helm/kartova/` — zero errors and zero warnings
- `helm template deploy/helm/kartova/ | kubectl apply --dry-run=client -f -` — no syntax errors

## Out of scope

Explicitly deferred, to prevent Slice 1 scope creep:

- **Auth / KeyCloak / JWT middleware** → Slice 2
- **RLS policies, multi-tenant isolation** → Slice 2
- **Wolverine handlers, Kafka routing, outbox usage** → Slice 3 (Wolverine is registered with persistence but idle)
- **KafkaFlow configuration** → Slice 3
- **Real domain entities in any module** → Slice 3 (Catalog has only the technical `KartovaMetadata` row)
- **Real UI with data** → Slice 3 (`/catalog` is a placeholder)
- **Full Helm chart templates** (ingress, HPA, NetworkPolicy, Secrets, ConfigMaps) → Slice 4
- **Audit log, GDPR export, soft delete** → Slice 5
- **Elasticsearch, MinIO setup** → Phase 1 or later
- **OpenTelemetry / Prometheus metrics export** → Slice 5 or Phase 1
- **Secret management (Vault / sealed-secrets / ExternalSecrets)** → Slice 4
- **Authored E2E (Playwright) and contract (Pact) tests** → Slice 3
- **GitOps / ArgoCD / Flux** → out of Phase 0 entirely
- **Mutation testing, property-based testing** → post-MVP
- **Production values.yaml with real resource limits / HPA config** → Slice 4
