# Kartova — Testing Strategy

**Scope:** the operational *how* of testing Kartova — what each tier is for, what "real seam" means, which fixtures to reuse, and what a slice owes before it's done.

**Ownership (do not duplicate):**
- **The decision** (five-tier pyramid, MSTest v4 + native asserts + NSubstitute, runner) lives in [ADR-0097](architecture/decisions/ADR-0097-mstest-supersedes-xunit.md). This doc never re-decides it.
- **The gates** (build / tests / mutation / reviews / DoD) live in [CLAUDE.md](../CLAUDE.md) → "Definition of Done." This doc never restates them — it explains how to *satisfy* gates 4, 5, and 7.
- **This doc** owns the operational guidance specs and plans anchor to.

---

## 1. The five tiers — what each is *for*

| Tier | Purpose | Catches what the tier below can't |
|------|---------|-----------------------------------|
| **Architecture** (NetArchTest, CI gate) | enforce static structure — module boundaries, DTO coverage attrs, REST/route policy | illegal cross-module references, missing `[ExcludeFromCodeCoverage]` |
| **Unit** | a single type in isolation; collaborators mocked with NSubstitute | branch logic, edge cases, pure-function correctness |
| **Integration** (Testcontainers) | the **assembled system at the real seam** — real pipeline, real Postgres/RLS, real JWT validation | wiring: filter/binding order, `SET LOCAL`/RLS, issuer/audience, transaction lifecycle |
| **Contract** (Pact) | provider/consumer compatibility across service boundaries | breaking API shape changes |
| **E2E** (Playwright) | a few critical user journeys through the real UI + API | top-of-stack regressions a human would notice |

**Altitude rule:** push every assertion to the *lowest* tier that can prove it. Use integration for what unit can't reach (the seam); keep E2E thin — it is the wrong tool to assert `SET LOCAL` or issuer/audience (too far from the seam, slow, flake-prone).

---

## 2. The real-seam rule (the heart of DoD gate 5)

A "wiring" slice — anything touching **HTTP / auth / DB / middleware / pipeline** — must be proven by **integration tests that exercise the real seam**, because unit and architecture tests run on the host against mocked boundaries and structurally cannot see assembly-level bugs (pipeline order, RLS, issuer/audience, restore gaps).

The real seam in this repo is **two levels** — pick the lowest that proves the slice:

### Level 1 — real pipeline + real Postgres + real JWT validation (default, fast)
`KartovaApiFixtureBase` (`WebApplicationFactory<Program>`) boots the **real assembled API** against a **real `postgres:18-alpine` Testcontainer** with the real role/grant/RLS seed, and swaps in `TestJwtSigner` via `UseTestJwtSigner`.

> ⚠️ `TestJwtSigner` is **not** a bypass auth handler. It runs the real `JwtBearerOptions` pipeline with `ValidateIssuer` / `ValidateAudience` / `ValidateLifetime` / `ValidateIssuerSigningKey` **all true** — only the signing key is a test key. So issuer/audience/lifetime validation, the real middleware order, and real RLS are all exercised. This is the default for almost every wiring slice.

Covers: filter-vs-binding order · JWT issuer/audience/lifetime · `SET LOCAL`/RLS isolation · transaction lifecycle.

### Level 2 — real KeyCloak container (opt-in, only when the slice touches KC)
Set `protected override bool UsesKeycloakContainer => true;` to additionally spin up a real KeyCloak Testcontainer (seeded from `deploy/keycloak/kartova-realm.json`). Use **only** when the slice exercises KeyCloak itself — the admin client (user provisioning, invitations) or real end-to-end token issuance / realm config. It is slower; don't pay for it when Level 1 proves the slice.

### What is *not* the seam
A test that mocks `DbContext`, or that replaces auth with an always-authenticate handler, is a **unit test wearing an integration costume** — green, trusted, and blind to every wiring bug. Mocked boundaries belong in the **unit** tier only.

---

## 3. Container-build tier (the one seam integration can't reach)

In-process integration tests build with `dotnet test` on the host — they never build the Docker image, so Dockerfile/restore gaps (missing csproj in the `COPY` layer, broken restore, bad `COPY` paths) are invisible to every tier above. The **`images` CI job** (`.github/workflows/ci.yml`) closes this by building the real images the way `docker-compose` does (`docker compose build migrator api` + `docker build web`).

**Rule:** any change to a `Dockerfile` or its `COPY` layer must be covered by the `images` gate. (This gate caught the API Dockerfile missing the Audit module — see commit `df5df56`.)

---

## 4. Mocking, coverage, mutation

- **Mocking:** NSubstitute, **unit tier only**. Never mock across the seam in integration tests — use the real container.
- **Coverage exclusions:** Contracts/DTOs/design-time factories/`*Module.cs`/test infra carry `[ExcludeFromCodeCoverage]` — enforced by `ContractsCoverageRules.cs` (see CLAUDE.md "Conventions").
- **Mutation gate:** ≥ **80%** on changed files (`stryker-config.json` `thresholds.high`). Driven by `/misc:mutation-sentinel` → `/misc:test-generator` (DoD gate 7).

---

## 5. Per-slice checklist — what a slice *owes*

State these as concrete tasks in the plan; verify at the DoD.

| If the slice wires… | It owes (integration tier, real seam) |
|---------------------|----------------------------------------|
| an HTTP endpoint | ≥1 happy-path + ≥1 negative-path test through `KartovaApiFixtureBase` (`CreateAuthenticatedClientAsync` + `CreateAnonymousClient` for the 401) |
| auth / roles | role-guard pass + reject (`401` anonymous, `403` wrong role); issuer/audience covered by `TestJwtSigner` |
| tenant-scoped DB (RLS) | per-tenant isolation test (tenant A insert, tenant B sees nothing) on the RLS `AppRole` connection |
| cross-tenant admin (BYPASSRLS) | a test on the `BypassRole` connection proving it sees across tenants — and that the RLS path still doesn't |
| KeyCloak admin / invitations / token issuance | opt into `UsesKeycloakContainer` (Level 2) |
| a new EF migration | migration applies cleanly in the fixture's `RunModuleMigrationsAsync` |
| a Dockerfile / `COPY` change | the `images` CI gate builds it |

---

## 6. Reuse map — don't reinvent the harness

| Need | Reuse |
|------|-------|
| Real pipeline + Postgres + JWT seam | `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs` — derive, override `RunModuleMigrationsAsync` |
| Real KeyCloak container | `tests/Kartova.Testing.Auth/KeycloakContainerFixture.cs` (opt in via `UsesKeycloakContainer`) |
| KC + Postgres aggregate (API-level tests) | `tests/Kartova.Api.IntegrationTests/KeycloakAndPostgresContainers.cs` |
| Test token minting / real JWT validation swap | `TestJwtSigner` + `TestAuthenticationExtensions.UseTestJwtSigner` |
| Authenticated / anonymous clients, deterministic tenant+sub | `CreateAuthenticatedClientAsync` / `CreateAnonymousClient` / `TenantFor` |
| Deserializing HTTP responses (camelCase + enum, ADR-0095) | `KartovaApiFixtureBase.WireJson` |
| Postgres roles / grants / migration bootstrap | `PostgresTestBootstrap` (`AppRole` / `BypassRole` / `MigratorRole`) |

### Harness conventions (learned the hard way)
- **One fixture per assembly:** wire it from `[AssemblyInitialize]`, with `[assembly: DoNotParallelize]` in `Properties/AssemblyInfo.cs` (the fixture mutates process-global env vars). **Not** per-class `[ClassInitialize]` — that creates one heavyweight Postgres+API fixture per derived class (~6× wall-clock regression; see `docs/superpowers/reviews/2026-05-09-feat-mstest-migration-phase-9-review.md`).
- Start independent containers (Postgres + KeyCloak) in parallel — wall-clock is `max`, not sum.
