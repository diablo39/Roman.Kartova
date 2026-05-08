# Kartova — Project Guide for Claude

**Product:** Kartova — SaaS service catalog + developer portal (Backstage + Compass + Statuspage hybrid)
**Owner:** Solo developer (Roman Głogowski), AI-assisted
**Stack:** .NET 10 (LTS) / ASP.NET Core + EF Core · Wolverine (CQRS mediator + outbound + outbox) · KafkaFlow (inbound Kafka consumers) · React + TypeScript · PostgreSQL 18 (RLS) · Elasticsearch · Apache Kafka (Strimzi/KRaft) · MinIO (S3) · KeyCloak (OIDC/JWT) · Kubernetes

## Where to find things

| Topic | File |
|-------|------|
| Product requirements (source of truth) | [docs/product/PRODUCT-REQUIREMENTS.md](docs/product/PRODUCT-REQUIREMENTS.md) |
| Backlog index (30 epics, 73 features, 209 stories) | [docs/product/EPICS-AND-STORIES.md](docs/product/EPICS-AND-STORIES.md) |
| Progress checklist | [docs/product/CHECKLIST.md](docs/product/CHECKLIST.md) |
| Phase files (0–9) | `docs/product/phases/phase-N-*.md` |
| ADR library + keyword index | [docs/architecture/decisions/README.md](docs/architecture/decisions/README.md) |
| Individual ADRs | `docs/architecture/decisions/ADR-NNNN-*.md` |
| ADR candidates (historical) | [docs/architecture/ADR-CANDIDATES.md](docs/architecture/ADR-CANDIDATES.md) |
| Design system (tokens, nav specs) | [docs/design/DESIGN.md](docs/design/DESIGN.md) |
| Google Stitch prompts | [docs/design/STITCH-PROMPTS.md](docs/design/STITCH-PROMPTS.md) |
| UI mockups (Stitch output, canonical) | `docs/ui-screens/{screen-name}/{code.html, screen.png}` |
| Per-slice implementation specs & plans | `docs/superpowers/specs/YYYY-MM-DD-*-design.md` + `docs/superpowers/plans/YYYY-MM-DD-*-plan.md` |

## Conventions

- Epic = `E-XX` · Feature = `E-XX.F-YY` · Story = `E-XX.F-YY.S-ZZ`
- ADR = `ADR-NNNN` (Michael Nygard template)
- Dates: always absolute (`2026-04-21`), never relative
- Windows shell: `cmd //c` (double slash — MSYS path-translation workaround) or PowerShell wrappers for `dotnet` commands in Git Bash. Git Bash lacks `grep -P` (use `-E` or `Select-String`)
- Solution file: `Kartova.slnx` (XML format, .NET 10 default — see ADR-0082). Classic `.sln` not used
- Coverage exclusion: every type in a `*.Contracts` assembly and every `*Dto`/`*Request`/`*Response` type in production code MUST carry `[ExcludeFromCodeCoverage]`. Pure data carriers, design-time `IDesignTimeDbContextFactory<>` factories, `IModule` composition classes (`*Module.cs` — DI/Wolverine wiring is composition-root code, parallel to `Program.cs` which is excluded from mutation), and test infrastructure are also excluded. Enforced by `tests/Kartova.ArchitectureTests/ContractsCoverageRules.cs` — adding a new DTO without the attribute fails the architecture suite.

## Architectural guardrails

Do/don't rules. For everything else, see [docs/architecture/decisions/README.md](docs/architecture/decisions/README.md).

- **CQRS mediator:** Wolverine — MediatR and MassTransit **not** used (ADR-0028, ADR-0080)
- **Messaging:** Apache Kafka (Strimzi/KRaft) — RabbitMQ/Redpanda **not** used (ADR-0003)
- **Database:** PostgreSQL 18 + Row-Level Security — **not** schema-per-tenant (ADR-0001, ADR-0012)
- **Search:** Elasticsearch shared index + per-tenant routing + filtered aliases — **not** index-per-tenant (ADR-0002, ADR-0013)
- **API style:** REST + OpenAPI 3.x — GraphQL **not** used (ADR-0029, ADR-0034)
- **Encryption:** column-level for OAuth tokens + TLS 1.2+ — mTLS **not** used (ADR-0077)
- **DB migrations:** dedicated `Kartova.Migrator` container (Helm pre-upgrade Job / Docker init) — **never** at app startup (ADR-0085)
- **Solution style:** modular monolith, one csproj tree per bounded context, NetArchTest enforced (ADR-0082)
- **Agent transport:** HTTPS polling — gRPC streaming **not** used (ADR-0042)
- **Frontend UI stack:** Untitled UI free-tier (react-aria-components + Tailwind v4) + @untitledui/icons (ADR-0094)
- **Testing:** five-tier pyramid — architecture (NetArchTest, mandatory CI gate) + unit + integration (Testcontainers) + contract (Pact) + E2E (Playwright). Framework: MSTest v4 + Microsoft.Testing.Platform; assertions: MSTest v4 native (no FluentAssertions); mocks: NSubstitute (ADR-0097, supersedes ADR-0083)

## Phases (MVP = 0–5)

0. Foundation (E-01) — scaffolding, CI/CD, auth, compliance, observability
1. Core Catalog & Notifications (E-02..E-06, E-06a)
2. Auto-Import (E-07..E-10) — GitHub, Azure DevOps, scorecards
3. Documentation (E-11) — markdown, OpenAPI, AsyncAPI
4. Status Page (E-12)
5. CLI, Policy & Billing (E-13, E-14, E-14a)

Post-MVP: 6 Agent · 7 Intelligence · 8 Analytics · 9 Advanced

## Working agreements

- **Before architectural suggestions:** check ADR keyword index in `docs/architecture/decisions/README.md`
- **Frontend / UI work:** read local mockup first from `docs/ui-screens/{screen}/code.html` + `screen.png` (canonical snapshot, per ADR-0087); escalate to Stitch MCP only when screen is missing locally or user asks for sync. Map Stitch HTML → shadcn/ui components (ADR-0088). Verify with Playwright MCP (**cold-start dev server first** — HMR cache can mask config errors — then navigate → interact → snapshot → check console) before claiming done (ADR-0084).
- **Before adding features:** verify they're not already scoped in `EPICS-AND-STORIES.md`; map each feature to its owning module (ADR-0082)
- **Tenant scope & DB access:** All tenant-scoped DB work runs inside `ITenantScope` (one open connection + transaction per request, `SET LOCAL app.current_tenant_id` on `Begin`). Register module DbContexts via `AddModuleDbContext<T>` — never raw `AddDbContext` for tenant-owned data. Transport adapters (ASP.NET endpoint filter, Wolverine/Kafka middleware) call `Begin`/`CommitAsync` — handlers never touch the scope. See ADR-0090.
- **Cross-module interactions:** only via Wolverine `IMessageBus` (in-process) or Kafka events; never direct references to other modules' Domain/Application/Infrastructure
- **List endpoints & list screens:** every new list endpoint exposes `sortBy` / `sortOrder` / `cursor` / `limit` and returns `CursorPage<T>` (ADR-0095). Every new list screen wires `useCursorList` + `useListUrlState` + `<DataTable>`. Treat as part of "first cut" — not a follow-up phase. Bounded lists may return flat arrays only when decorated with `[BoundedListResult]` + inline justification.
- **When proposing new ADRs:** preview decision before saving (user reviews first)
- **Implementation work:** Superpowers workflow — `superpowers:brainstorming` → `docs/superpowers/specs/YYYY-MM-DD-*-design.md`, then `superpowers:writing-plans` → `docs/superpowers/plans/YYYY-MM-DD-*-plan.md`, then `superpowers:executing-plans` (ticks checkboxes in-place). Roadmap/scope lives in `docs/product/` (EPICS-AND-STORIES.md, CHECKLIST.md, phases/). **GSD is not used** — existing product docs already cover milestone/phase-level tracking.
- **Per slice:** scope one vertical slice end-to-end (walking skeleton → auth → first CRUD → CI/CD+helm → compliance). After executing a plan, update `docs/product/CHECKLIST.md` to reflect completed stories.
- **Compliance:** GDPR + MiFID II from day one — not bolted on later (see E-01.F-05)
- **Definition of Done:** An implementation is "complete" / "finished" / "ready to merge" only when ALL of the following are green and can be cited by command + output:
  1. Full solution build with `TreatWarningsAsErrors=true` (0 warnings, 0 errors).
  2. Per-task subagent reviews (spec-compliance + code-quality) executed — **never skipped on grounds of "trivial"**; review is cheap and its purpose is to force the pause that catches rationalization.
  3. `superpowers:requesting-code-review` invoked at slice boundary against the **full branch diff** with spec + plan as context — catches cross-task design issues the per-task loop can't see (e.g., interaction between filter defined in Task N and wiring in Task M).
  4. Full test suite green: unit + architecture + integration (Testcontainers).
  5. For any slice that wires HTTP / auth / DB / middleware / pipeline: at least one `docker compose up` + real HTTP happy-path + one negative-path, output captured and confirmed. Unit + architecture tests alone are the wrong layer of evidence for these slices — they won't catch filter-vs-binding order, JWT issuer/audience, `SET LOCAL` semantics, or Dockerfile restore gaps.
  6. `/simplify` skill run against the branch diff — surfaces reuse, code-quality, and efficiency findings the spec-and-quality reviews don't target. Should-fix items from each of the three review lenses (reuse / quality / efficiency) addressed or explicitly skipped with a reason.
  7. Mutation feedback loop run on changed files: `mutation-sentinel` (find surviving mutants) → `test-generator` (strengthen tests until mutants are killed). Mutation score must meet the repo target (≥80% per `stryker-config.json`). Document the score and any surviving mutants accepted as low-value.
  8. `/pr-review-toolkit:review-pr` skill
  9. `/deep-review` skill run against the branch diff with spec / plan / ADRs / tests as context — produces a fixed-schema report (Blocking / Should-fix / Nits / Missing tests / What looks good). Blocking and Should-fix items addressed before merge; nits triaged.
  
  Until all nine are green, the honest status is **"implementation staged, verification pending"** — never "slice N complete". If a step cannot be run locally (e.g., Docker unavailable on this machine), say so explicitly and flag as *pending user verification*, never imply completion. A Stop hook at `.claude/hooks/dod-check.js` blocks turns that assert completion without citing verification evidence.
