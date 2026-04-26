# Slice 2 Design Spec — Auth + Multi-Tenancy

**Date:** 2026-04-22
**Status:** Approved
**Phase:** 0 (Foundation), Slice 2 of per-slice sequence (walking skeleton → **auth** → first CRUD → CI/CD+helm → compliance)
**Scope:** End-to-end tenant-scoped request path: KeyCloak auth, JWT validation, tenant-claim extraction, per-request tenant scope with PostgreSQL RLS, Organization module, role-based authorization on one marker endpoint, admin-only `POST /organizations` bypass path. Integration test infrastructure (test JWT signer + one KeyCloak testcontainer smoke test). Two new ADRs drafted (ADR-0090 tenant-scope mechanism, ADR-0091 already saved).

## Problem

Slice 1 produced a running walking skeleton with anonymous endpoints (`/health`, `/version`) and an empty Catalog module. Before any feature work, the platform needs:

1. **Authenticated requests** — every non-health endpoint protected by JWT from a real IdP (KeyCloak, ADR-0006).
2. **Tenant isolation at the DB layer** — PostgreSQL Row-Level Security (ADR-0012) enforced per-request with `app.current_tenant_id` sourced from the JWT (ADR-0014).
3. **A reusable mechanism, not per-endpoint plumbing** — the tenant-scope code lives in exactly one place; new endpoints/transports inherit the enforcement.
4. **Defense-in-depth correctness** — connection-pool safe, durability-correct (commit before response), atomicity preserving (Wolverine outbox pattern, ADR-0080, is not broken).
5. **Tests proving the above** — architecture tests, integration tests with multi-tenant seed, a KeyCloak-container auth smoke test.

Slice 2 addresses exactly these items and establishes the abstractions every later slice will rely on (`ITenantScope`, `AddModuleDbContext`, `TenantScopeEndpointFilter`, claims transformation, test JWT signer).

## Decisions

| Topic | Decision |
|-------|----------|
| Signup handling | **Defer self-service signup to Phase 2 E-09.** Seed the dev realm with pre-configured orgs + users. `POST /api/v1/admin/organizations` is admin-only via `platform-admin` role on a BYPASSRLS DB role — used during MVP to create tenants for dev/demo. |
| KeyCloak realm provisioning | Committed `deploy/keycloak/kartova-realm.json`; imported via `--import-realm` on container start. Same JSON reused by the KeyCloak testcontainer in the auth smoke test. |
| KeyCloak image | Official `quay.io/keycloak/keycloak:<pinned-version>` (pin to latest stable at implementation time, e.g. `26.x`). No Bitnami, no custom fork. |
| `tenant_id` claim source | KeyCloak **user attribute** `tenant_id` → protocol mapper emits it as a top-level JWT claim. Not group-based. Simpler 1:1 with ADR-0011 (one org per user session). |
| Test JWT issuer shape | RSA-2048 key generated at test fixture startup. Tokens have issuer `https://test-issuer.kartova.local`, audience `kartova-api`, standard `exp`, `iat`, `sub`, `tenant_id`, `realm_access.roles` claims — matches KeyCloak shape to minimize drift. |
| Tenant-scope mechanism | `ITenantScope` (one open `NpgsqlConnection` + one transaction per request) — `SET LOCAL app.current_tenant_id` at `Begin`, commit before response/ack. All module DbContexts share that connection + enlist in the transaction. **Drafted as ADR-0090 in Section 10 below; saved as real ADR during implementation.** |
| Bypass path for `POST /organizations` | Separate `AdminOrganizationDbContext` configured with a DB role that has `BYPASSRLS`. No tenant scope, no `SET LOCAL`. Isolated to `Kartova.Organization.Infrastructure.Admin` assembly; architecture test enforces no other assembly uses this role. |
| Error response shape | RFC 7807 `application/problem+json` via ASP.NET `AddProblemDetails()`. Already saved as **ADR-0091**. |
| RBAC scope in this slice | Two roles wired: `OrgAdmin` (tenant-scoped), `platform-admin` (non-tenant, for admin bypass). Full 5-role taxonomy (ADR-0008) lands incrementally as endpoints appear. |
| Tenant-scoped proof endpoint | `GET /api/v1/organizations/me` — reads from `organizations` table with RLS policy. One endpoint sufficient to prove the mechanism end-to-end. |
| Dev realm contents | 2 orgs, 3 users: Org A (admin@orga + member@orga), Org B (admin@orgb). Enables visceral cross-tenant isolation demo via Postman. |
| Integration test strategy | **Hybrid**: local RSA test JWT signer for every integration test (fast, ~5ms per token); one KeyCloak testcontainer smoke test per CI build (~30s) that proves realm JSON + claim mapper + JwtBearer pipeline work end-to-end with a real KeyCloak. |
| Project layering | `Kartova.SharedKernel` (abstractions only), `Kartova.SharedKernel.Postgres` (EF/Npgsql impl), `Kartova.SharedKernel.AspNetCore` (filter + claims + auth ext), `Kartova.SharedKernel.Wolverine` (middleware skeleton — for future slices). Architecture test enforces no tech deps in `SharedKernel`. |
| DB role for `BYPASSRLS` | New role `kartova_bypass_rls` in `docker/postgres/init.sql` (`CREATEROLE` not needed, just `BYPASSRLS` + read/write grants on relevant tables). |
| Connection pool sizing | Defer as separate operational ADR. At MVP solo-dev scale, default Npgsql pool (max 100) is sufficient. Future PgBouncer/pool ADR when load profile demands. |
| SPA login flow | Out of scope. Tenant endpoints exercised via Postman/Bruno/HTTP clients using KeyCloak ROPC grant (for manual testing) or the test signer (for automated tests). SPA login lands in a later slice after Slice 3's first CRUD. |
| Solution structure | New projects added to `Kartova.slnx`: `Kartova.SharedKernel.Postgres`, `Kartova.SharedKernel.AspNetCore`, `Kartova.SharedKernel.Wolverine`, `Kartova.Organization.{Domain,Application,Infrastructure,Contracts,Tests,IntegrationTests}`, `Kartova.Testing.Auth`, `Kartova.Api.IntegrationTests`. |

## Architecture

### 3.1 Project layout

```
src/
  Kartova.SharedKernel/                         (existing — add Multitenancy/ folder)
    Multitenancy/
      TenantId.cs                               record struct
      ITenantContext.cs                         { TenantId Id; bool IsTenantScoped; IReadOnlyCollection<string> Roles }
      ITenantScope.cs                           { Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct); }
      IAsyncTenantScopeHandle.cs                IAsyncDisposable + Task CommitAsync(CancellationToken ct)
      ITenantOwned.cs                           marker interface for tenant-scoped entities (used by arch tests)

  Kartova.SharedKernel.Postgres/                (new)
    TenantScope.cs                              implements ITenantScope; holds NpgsqlConnection + IDbContextTransaction
    EnlistInTenantScopeInterceptor.cs           DbContext initializer → UseTransactionAsync(scope.Tx)
    TenantScopeRequiredInterceptor.cs           SaveChangesInterceptor — throw if no scope
    AddModuleDbContextExtensions.cs             AddModuleDbContext<T>(services) helper

  Kartova.SharedKernel.AspNetCore/              (new)
    JwtAuthenticationExtensions.cs              AddKartovaJwtAuth(IConfiguration) — OIDC discovery + JwtBearer
    TenantClaimsTransformation.cs               IClaimsTransformation → populates ITenantContext
    TenantScopeEndpointFilter.cs                Begin(tenant) → next → CommitAsync
    TenantScopeRouteExtensions.cs               .RequireTenantScope() — adds filter + RequireAuthorization
    ProblemTypes.cs                             const slug registry for ADR-0091

  Kartova.SharedKernel.Wolverine/               (new, skeleton)
    TenantScopeMiddleware.cs                    Before/After/OnException — not exercised in Slice 2 but registered

  Modules/
    Organization/                                (new module)
      Kartova.Organization.Domain/
        Organization.cs                          aggregate (id, tenantId, name, createdAt)
        OrganizationId.cs                        record struct
      Kartova.Organization.Application/
        GetCurrentOrganizationQuery.cs           MediatR-less: method on application service
        OrganizationApplicationService.cs
      Kartova.Organization.Infrastructure/
        OrganizationDbContext.cs                 implements ITenantOwned on entity config
        OrganizationEntityTypeConfiguration.cs
        Migrations/
          YYYYMMDDHHMMSS_InitialOrganization.cs  create organizations + RLS + index
        IOrganizationRepository.cs
        OrganizationRepository.cs
        Admin/
          AdminOrganizationDbContext.cs          separate DbContext using BYPASSRLS connection string
          AdminOrganizationRepository.cs
      Kartova.Organization.Contracts/
        OrganizationDto.cs
      OrganizationModule.cs                      IModule — AddModuleDbContext<OrganizationDbContext>()
                                                   + AddScoped<IOrganizationRepository,...>()
                                                   + AddScoped<AdminOrganizationDbContext>()
      Kartova.Organization.Tests/                unit
      Kartova.Organization.IntegrationTests/     Testcontainers.PostgreSql

  Kartova.Api/                                   (existing — extend Program.cs)
    Program.cs
      AddKartovaJwtAuth(builder.Configuration)
      AddProblemDetails() with traceId customizer
      AddScoped<ITenantContext, TenantContextAccessor>()
      AddScoped<ITenantScope, TenantScope>()
      AddModuleDbContext<OrganizationDbContext>()
      ... per-module registration via module IModule list
      MapGroup("/api/v1").RequireAuthorization().AddEndpointFilter<TenantScopeEndpointFilter>()
        .MapOrganizationEndpoints()
      MapGroup("/api/v1/admin").RequireAuthorization(policy => policy.RequireRole("platform-admin"))
        .MapAdminOrganizationEndpoints()

tests/
  Kartova.ArchitectureTests/                     (existing — add rules)
  Kartova.Testing.Auth/                          (new)
    TestJwtSigner.cs
    TestJwtAuthenticationHandler.cs
    SeededOrgs.cs                                test constants
  Kartova.Api.IntegrationTests/                  (new, auth smoke only — 1 test)
    KeycloakContainerFixture.cs
    AuthSmokeTests.cs

deploy/
  keycloak/
    kartova-realm.json                           2 orgs, 3 users, mapper, client
docker/
  postgres/
    init.sql                                     (existing — add kartova_bypass_rls role)

docker-compose.yml                               (existing — add keycloak + keycloak-db)
```

### 3.2 Dependency graph

```
Kartova.SharedKernel               (zero framework deps)
  ↑
Kartova.SharedKernel.Postgres      → Npgsql + EFCore.Npgsql
Kartova.SharedKernel.AspNetCore    → ASP.NET Core
Kartova.SharedKernel.Wolverine     → Wolverine
  ↑
Kartova.Api                        composes all three + module Infrastructure
Module.Infrastructure              → SharedKernel + SharedKernel.Postgres
Module.Application / Module.Domain → SharedKernel only
```

### 3.3 Database

```sql
-- docker/postgres/init.sql additions
CREATE ROLE kartova_bypass_rls LOGIN PASSWORD 'dev_only' BYPASSRLS;
GRANT USAGE ON SCHEMA public TO kartova_bypass_rls;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO kartova_bypass_rls;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_bypass_rls;
```

```sql
-- InitialOrganization migration (EF generated + raw SQL for RLS)
CREATE TABLE organizations (
  id          uuid PRIMARY KEY,
  tenant_id   uuid NOT NULL,
  name        text NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_organizations_tenant ON organizations(tenant_id);

ALTER TABLE organizations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organizations FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON organizations
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
```

## Components & data flow

### 4.1 Tenant-scoped HTTP request — sequence

```
Client → GET /api/v1/organizations/me + Bearer <JWT>
  ↓
UseAuthentication() — JwtBearerHandler validates against KeyCloak JWKS
  ↓
TenantClaimsTransformation — reads tenant_id + roles → populates scoped ITenantContext
  ↓
UseAuthorization() — [Authorize] passes
  ↓
Route group filter: TenantScopeEndpointFilter
    scope.BeginAsync(tenantContext.Id, ct)
      → dataSource.OpenConnectionAsync → NpgsqlConnection from pool
      → connection.BeginTransactionAsync → IDbContextTransaction
      → "SET LOCAL app.current_tenant_id = $1" with id
  ↓
Endpoint handler resolves OrganizationDbContext (scoped DI)
  → factory pulls NpgsqlConnection from scope
  → EnlistInTenantScopeInterceptor → UseTransactionAsync(scope.Tx)
  → LINQ query runs inside tx; RLS filters + EF global query filter apply
  ↓
Handler returns Results.Ok(OrganizationDto)
  ↓
Filter resumes: scope.CommitAsync(ct)
  → COMMIT; Postgres discards SET LOCAL server-side; connection returns to pool clean
  ↓
ASP.NET writes response body
```

### 4.2 Cross-tenant isolation sequence (integration test)

```
Seed (via BYPASSRLS repo):
  org_A = { id=A, tenant_id=A, name='A' }
  org_B = { id=B, tenant_id=B, name='B' }

Token A = TestJwtSigner.IssueForOrg(A, ['OrgAdmin'])
Token B = TestJwtSigner.IssueForOrg(B, ['OrgAdmin'])

GET /api/v1/organizations/me + Token A → 200 { id: A, name: 'A' }
GET /api/v1/organizations/me + Token B → 200 { id: B, name: 'B' }

// Raw BYPASSRLS query: SELECT count(*) FROM organizations → 2 (both present; RLS just hid them)
```

### 4.3 Admin bypass path — `POST /api/v1/admin/organizations`

```
Client (platform-admin token, NO tenant_id claim)
  ↓
UseAuthentication() OK
TenantClaimsTransformation — ITenantContext.IsTenantScoped = false
UseAuthorization() — RequireRole("platform-admin") passes
  ↓
Route is under MapGroup("/api/v1/admin") — NO TenantScopeEndpointFilter attached
  ↓
Handler resolves AdminOrganizationDbContext
  → configured with NpgsqlConnection via kartova_bypass_rls role connection string
  → no ITenantScope involvement
  → INSERT organizations (new_uuid, new_uuid, name, now())
  ↓
201 Created + Location: /api/v1/organizations/{id}
```

### 4.4 Unauthenticated endpoint (`/health`, `/version`)

```
Route NOT inside RequireAuthorization group
  ↓
Handler runs directly, no DbContext resolved
  ↓
Returns 200 / 503
```

## Error handling

All error responses use RFC 7807 per ADR-0091.

| Failure | Status | Problem type slug |
|---|---|---|
| No `Authorization` header on protected route | 401 | (standard Bearer challenge, no body) |
| Invalid / expired / tampered JWT | 401 | `invalid-token` |
| Valid JWT but missing `tenant_id` claim on tenant-scoped route | 401 | `missing-tenant-claim` |
| Valid token, authenticated, role check fails | 403 | `forbidden` |
| Tenant token tries to access another tenant's resource | 404 | `resource-not-found` (RLS filters → null → 404, NOT 403) |
| `Begin` fails (DB down, pool exhausted) | 503 | `service-unavailable` |
| Handler throws business exception | 422 or 409 | per domain-specific registry |
| `CommitAsync` throws | 500 | `internal-server-error` |
| `ITenantScope` not active when DbContext used (bug) | 500 | caught by arch test first; runtime fallback returns 500 with `tenant-scope-required` |

Durability guarantees:
- Commit happens inside filter's `finally`-like flow **before** `next(ctx)` returns the Result — which means before ASP.NET serializes + writes the response body.
- Commit failure propagates → ASP.NET pipeline → 500 problem-details. Client sees failure, no data committed.
- Handler exceptions → `DisposeAsync` on scope → ROLLBACK. Exception rethrown.

## Testing

### 6.1 Architecture tests (`Kartova.ArchitectureTests`, new rules)

| Rule | Intent |
|---|---|
| `SharedKernel` has no deps on Npgsql, EFCore, AspNetCore, Wolverine, KafkaFlow | Layering |
| Module DbContexts registered only via `AddModuleDbContext<T>` (no raw `AddDbContext<T>` for module types) | Enforce scope wiring |
| `ITenantScope.BeginAsync` only called from transport adapter projects | No handler-level bypass |
| Every entity implementing `ITenantOwned` has RLS policy in some migration | Defense-in-depth |
| Only `Kartova.Organization.Infrastructure.Admin` references `kartova_bypass_rls` connection string / BYPASSRLS role | Bypass isolation |
| No endpoint returns `Results.Json(new { error = ... })` or similar ad-hoc error shapes | Enforce ADR-0091 |
| `TestJwtSigner` not referenced outside test assemblies | Prevent leak to prod |

### 6.2 Unit tests (`Kartova.Organization.Tests`)

- Organization aggregate invariants
- Claims transformation: `ClaimsPrincipal` → `ITenantContext` (various shapes: no claim, invalid UUID, multiple roles)
- Application service handler (GetCurrentOrganizationQuery) with mocked repository

### 6.3 Integration tests (`Kartova.Organization.IntegrationTests`, Testcontainers.PostgreSql + TestJwtSigner)

| Test | Proves |
|---|---|
| `OrganizationReadEndpoint_ReturnsCurrentTenantRow` | Happy path, end-to-end with test token |
| `TenantIsolation_CrossTenantReturnsEmpty` | Token A cannot see Org B row |
| `TenantIsolation_RawSqlProvesDataExists` | BYPASSRLS role sees both rows |
| `NoTenantScope_QueryThrowsFromInterceptor` | DbContext outside scope → InvalidOperationException |
| `CommitFailsAfterHandler_Returns500_NoDataCommitted` | Commit-time failure → 500 + rollback |
| `ExceptionDuringHandler_RollsBack` | Rollback on handler exception |
| `AdminBypassPath_CreateOrganization_WorksWithoutTenantScope` | Admin endpoint creates org, no scope needed |
| `AdminBypassPath_RejectsNonPlatformAdmin` | 403 for non-admin |
| `JwtValidation_ExpiredToken_401` | `exp` in past → 401 |
| `JwtValidation_MissingTenantClaim_OnTenantRoute_401` | problem-details `missing-tenant-claim` |
| `RoleEnforcement_OrgAdminRoute_NonAdminGets403` | RBAC smoke |

### 6.4 Auth smoke test (`Kartova.Api.IntegrationTests`, Testcontainers KeyCloak, 1 test)

```
Full_KeyCloak_Realm_Issues_Token_API_Accepts:
  1. Start KeyCloak container with --import-realm=kartova-realm.json (~20–30s)
  2. ROPC grant as seeded Org A admin user
  3. Extract access_token
  4. GET /api/v1/organizations/me + Bearer <token>
  5. Assert 200 + body.id == Org A id
```

### 6.5 Test inventory

| Tier | New | Reused from Slice 1 |
|---|---|---|
| Architecture | ~8 | 6 |
| Unit | ~12 | 2 |
| Integration | ~11 | 2 |
| Auth smoke | 1 | 0 |
| E2E | 0 | 0 |

All green in CI per ADR-0083.

## Deployment (local)

### 7.1 docker-compose additions

```yaml
services:
  keycloak-db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: keycloak
      POSTGRES_USER: keycloak
      POSTGRES_PASSWORD: keycloak_dev
    volumes: [keycloak-db-data:/var/lib/postgresql/data]

  keycloak:
    image: quay.io/keycloak/keycloak:<PIN_DURING_IMPL>
    command: ["start-dev", "--import-realm"]
    environment:
      KC_DB: postgres
      KC_DB_URL: jdbc:postgresql://keycloak-db:5432/keycloak
      KC_DB_USERNAME: keycloak
      KC_DB_PASSWORD: keycloak_dev
      KEYCLOAK_ADMIN: admin
      KEYCLOAK_ADMIN_PASSWORD: admin_dev
    ports: ["8080:8080"]
    volumes:
      - ./deploy/keycloak/kartova-realm.json:/opt/keycloak/data/import/kartova-realm.json:ro
    depends_on: [keycloak-db]

  api:
    # existing — add env
    environment:
      Authentication__Authority: http://keycloak:8080/realms/kartova
      Authentication__Audience: kartova-api
      Authentication__MetadataAddress: http://keycloak:8080/realms/kartova/.well-known/openid-configuration
```

### 7.2 Realm JSON contents (summary)

- Realm: `kartova`
- Client: `kartova-api` (public + direct access grants enabled for dev ROPC)
- Users:
  - `admin@orga.kartova.local` / `dev_pass` — attributes: `tenant_id=<OrgA-uuid>`; realm roles: `OrgAdmin`
  - `member@orga.kartova.local` / `dev_pass` — attributes: `tenant_id=<OrgA-uuid>`; no roles
  - `admin@orgb.kartova.local` / `dev_pass` — attributes: `tenant_id=<OrgB-uuid>`; realm roles: `OrgAdmin`
  - `platform-admin@kartova.local` / `dev_pass` — no tenant_id attribute; realm roles: `platform-admin`
- Protocol mappers:
  - User-attribute mapper: `tenant_id` → top-level `tenant_id` claim
  - Realm-role mapper: `realm_access.roles` (standard)

## Success criteria

1. `docker-compose up` brings up postgres + keycloak-db + keycloak + migrator + api healthy.
2. Manual: ROPC grant against KeyCloak yields access token with `tenant_id` claim.
3. Manual: `GET /api/v1/organizations/me` with Org A token → 200 + Org A row; same with Org B → 200 + Org B row; without token → 401; wrong role on admin endpoint → 403.
4. All architecture tests green (new + existing).
5. All integration tests green (11 new + 2 existing).
6. Auth smoke test green in CI (~30s).
7. CI pipeline passes with all new assemblies compiled + tested.
8. ADR-0090 saved, README index updated, change log updated.
9. ADR-0091 already saved (pre-committed).
10. `CHECKLIST.md` updated: E-01.F-03.S-01 (multi-tenant schema), E-01.F-04.S-01 (KeyCloak), E-01.F-04.S-02 (JWT middleware), E-01.F-08.S-03 (RLS isolation verified) marked complete or partial.

## Out of scope (deferred)

- SPA OIDC login flow — later slice (dedicated frontend-auth slice).
- Self-service signup — Phase 2 E-09 (Onboarding Wizard).
- Full 5-role RBAC taxonomy beyond OrgAdmin + platform-admin — grows with features.
- First Catalog CRUD — Slice 3.
- PgBouncer / connection-pool sizing ADR — separate operational concern.
- Rate limiting (ADR-0031), audit log (ADR-0018) — separate Phase 0 slices.
- KeyCloak Helm chart deployment — separate Phase 0 ops story.
- Token refresh UI / silent auth — SPA concern.
- Contract tests (Pact), Playwright E2E — when SPA auth lands.
- Multi-org-per-user session switching — post-MVP.

## Risks & mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Realm JSON drifts from auth-smoke test expectations | Medium | Smoke test fails after realm edits | Smoke test runs on every PR; realm JSON reviewed whenever auth code changes |
| Connection-pool exhaustion at MVP scale | Low | API 503s | Defer; default pool size 100 adequate for solo-dev load |
| `SET LOCAL` semantics change in future Postgres | Very low | Core mechanism breaks | Revisit ADR-0090 if ever announced |
| Test JWT signer leaks into prod | Low | Auth bypass | Architecture test + env-gated registration (`TestJwtSigner` only wired in Test/Development envs) |
| Developer uses raw `AddDbContext<T>` for a module | Medium | Silent RLS bypass | Architecture test breaks CI on PR |
| Admin bypass role misused beyond admin assembly | Medium | Cross-tenant leak via bypass | Architecture test + single-use connection string |

## ADR-0090 draft (to be saved to `docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md` during implementation)

Full content below — reviewed here as part of spec approval; extracted to its own file as Task 1 of implementation.

---

**ADR-0090: Tenant Scope Mechanism — Transaction-Bound `SET LOCAL` with Shared Connection per Request**

**Status:** Accepted
**Date:** 2026-04-22
**Category:** Multi-Tenancy
**Related:** ADR-0006, ADR-0011, ADR-0012, ADR-0014, ADR-0080, ADR-0082

### Context

ADR-0012 mandates PostgreSQL Row-Level Security with `app.current_tenant_id` as the RLS policy input. ADR-0014 mandates extracting tenant from JWT per request. The question this ADR answers: **where and how** the GUC is set on each request so RLS works, without pool leaks, without breaking atomicity (ADR-0080 outbox), without per-handler code, and with durability correctness (commit before response).

Candidate patterns considered:

- **`DbConnectionInterceptor.ConnectionOpenedAsync` + plain `SET`** — pool-unsafe (values stick on pooled connections between requests); some implementations disable pooling entirely (e.g., bytefish.de ASP.NET multi-tenancy article uses `Pooling=false`), which doesn't meet ADR-0074 scale targets.
- **`DbConnectionInterceptor` + SET on open + RESET on close** — relies on .NET code paths running on every close, leak window under crash / close-hook failure.
- **Pinned connection for DbContext lifetime + SET in constructor** — holds pool slot for full request (including response serialization), worse pool utilization; still relies on RESET in dispose.
- **Per-command transactions in `DbCommandInterceptor`** — breaks atomicity; incompatible with ADR-0080 transactional outbox.
- **Commit on `DbContext.DisposeAsync`** — durability bug: response flushed to client before dispose; commit failures silently lost.
- **Per-entry-point middleware duplicating tx + SET LOCAL logic** — DRY violation; every new transport adds risk.

### Decision

Introduce `ITenantScope` as the single abstraction owning the tenant-isolation mechanism.

**API:**
```csharp
public interface ITenantScope
{
    Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct);
}
public interface IAsyncTenantScopeHandle : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
```

**Implementation** (`Kartova.SharedKernel.Postgres.TenantScope`, scoped DI):
- `BeginAsync`: opens `NpgsqlConnection` from `NpgsqlDataSource`, begins transaction, issues `SET LOCAL app.current_tenant_id = <id>`.
- `CommitAsync`: commits the transaction. Failures propagate to caller.
- `DisposeAsync`: rolls back if not committed.

**All module DbContexts** register via `AddModuleDbContext<T>` which:
- Reads the scope's `NpgsqlConnection` via factory delegate in DI.
- Adds `EnlistInTenantScopeInterceptor` to enlist in the scope's transaction on DbContext first use.
- Adds `TenantScopeRequiredInterceptor` (SaveChangesInterceptor) as fail-fast assertion.

**Each transport has exactly one adapter** that calls `Begin` / `CommitAsync`:
- ASP.NET: `TenantScopeEndpointFilter` (via `.AddEndpointFilter<>` on the tenant-scoped route group) — commits before response body flush.
- Wolverine: `TenantScopeMiddleware` (Before/After/OnException) — commits before message ack.
- Future transports: one middleware each, same pattern.

**Tenant claim population** is transport-specific (JWT claim, Kafka header, etc.) and handled by a separate lightweight adapter (e.g., `TenantClaimsTransformation` for ASP.NET); transport adapters call `ITenantContext.Id` into `BeginAsync`.

### Rationale

- **Postgres-native cleanup** — `SET LOCAL` is discarded by Postgres on `COMMIT`/`ROLLBACK`. Process crash, connection fault, skipped dispose, exception path: the server handles it, not .NET code. Pool-safe without relying on Npgsql `DISCARD ALL` or pool disable.
- **Durability correctness** — commit happens in the transport filter *before* response flush / ack. Commit failures become HTTP 500 / message retry, not silent data loss.
- **Atomicity preserved** — one request = one transaction. Outbox inserts (ADR-0080) enlist in the same tx. Multi-DbContext handlers (modular monolith) share connection + tx → cross-module atomic.
- **DRY** — one `ITenantScope` implementation; per-transport adapters are tiny and cross-cutting (registered once). New endpoints inherit enforcement by using the DbContext normally; no handler-level plumbing.
- **Defense-in-depth** — server-side RLS + EF global query filter + `TenantScopeRequiredInterceptor` fail-fast + architecture tests enforcing the pattern. Multiple independent layers.

### Alternatives Considered

See "Context" — each is explicitly rejected above with its specific failure mode documented.

### Consequences

**Positive:**
- No per-handler boilerplate.
- Crash-safe cleanup without pool-config assumptions.
- Commit failures observable by callers.
- Multi-DbContext atomicity for modular monolith.

**Negative / Trade-offs:**
- Holds one `NpgsqlConnection` for the full HTTP request duration (including response serialization). At MVP solo-dev scale not a concern; at 1000-tenant target, connection pool sizing + PgBouncer will be a future operational ADR.
- Every module DbContext must use `AddModuleDbContext<T>` — enforced by architecture test.
- Admin bypass paths (e.g., `POST /api/v1/admin/organizations`) run *outside* tenant scope and require a separate DbContext with BYPASSRLS role. Pattern documented; enforcement via isolated assembly + architecture test.

**Neutral:**
- Secondary stores (Elasticsearch per ADR-0013, Kafka per ADR-0003, MinIO per ADR-0004) have their own tenant isolation and do not participate in the Postgres transaction.
- Wolverine outbox (ADR-0080) composes naturally — outbox insert is part of the same transaction as domain writes.

### Implementation notes

- `ITenantScope` scoped in DI; per request.
- `NpgsqlDataSource` registered as singleton; `BeginAsync` acquires connection from it.
- DbContexts registered with `UseNpgsql(scope.Connection)` pattern via `AddModuleDbContext<T>` helper.
- `EnlistInTenantScopeInterceptor.ContextInitializingAsync` calls `Database.UseTransactionAsync(scope.Transaction.GetDbTransaction(), ct)`.
- Architecture tests enforce: no raw `AddDbContext<T>` for module types; `ITenantScope.BeginAsync` only called from transport adapters.
- Bypass path uses `AdminOrganizationDbContext` configured with `kartova_bypass_rls` role connection string; isolated assembly `Kartova.Organization.Infrastructure.Admin`.

### References

- ADR-0006 (KeyCloak), ADR-0011 (1 org = 1 tenant), ADR-0012 (RLS), ADR-0014 (tenant claim from JWT), ADR-0080 (Wolverine outbox), ADR-0082 (modular monolith).
- PostgreSQL docs: `SET LOCAL`, customized options, Row Security Policies.
- Crunchy Data: "Row-Level Security for Tenants in Postgres" — validates the `current_setting('app.xxx')` community idiom.
- bytefish.de "ASP.NET Core Multi-Tenancy" — alternative considered, rejected due to `Pooling=false`.
