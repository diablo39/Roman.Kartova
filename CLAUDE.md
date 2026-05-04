# Kartova — Project Guide for Claude

**Product:** Kartova — SaaS service catalog + developer portal (Backstage + Compass + Statuspage hybrid)
**Owner:** Solo developer (Roman Głogowski), AI-assisted
**Stack:** .NET 10 (LTS) / ASP.NET Core + EF Core · Wolverine (CQRS mediator + outbound + outbox) · KafkaFlow (inbound Kafka consumers) · React + TypeScript · PostgreSQL 16 (RLS) · Elasticsearch · Apache Kafka (Strimzi/KRaft) · MinIO (S3) · KeyCloak (OIDC/JWT) · Kubernetes

## Where to find things

| Topic | File |
|-------|------|
| Product requirements (source of truth) | [docs/product/PRODUCT-REQUIREMENTS.md](docs/product/PRODUCT-REQUIREMENTS.md) |
| Backlog index (30 epics, 73 features, 209 stories) | [docs/product/EPICS-AND-STORIES.md](docs/product/EPICS-AND-STORIES.md) |
| Progress checklist | [docs/product/CHECKLIST.md](docs/product/CHECKLIST.md) |
| Phase files (0–9) | `docs/product/phases/phase-N-*.md` |
| ADR library (89 accepted) + keyword index | [docs/architecture/decisions/README.md](docs/architecture/decisions/README.md) |
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

## Key architectural decisions

Quick lookup; full context in the ADR library keyword index.

| Area | Decision | ADR |
|------|----------|-----|
| Backend | .NET 10 (LTS) + ASP.NET Core + EF Core, Clean Architecture per module | ADR-0027, ADR-0028 |
| Solution style | Modular monolith — one csproj tree per bounded context, NetArchTest fitness functions enforce boundaries | ADR-0082 |
| Testing | Five-tier pyramid: architecture (NetArchTest, mandatory CI gate) + unit (xUnit) + integration (Testcontainers) + contract (Pact) + E2E (Playwright) | ADR-0083 |
| Frontend dev workflow | Full loop: Google Stitch MCP (design source) → implementation → Playwright MCP (verify) → commit | ADR-0084, ADR-0087 |
| Frontend | React SPA + TypeScript strict, Vite, TanStack Query | ADR-0039 |
| Frontend UI stack | Untitled UI free-tier (react-aria-components + Tailwind CSS v4) + @untitledui/icons; nav canonical in DESIGN.md | ADR-0094 |
| Database | PostgreSQL 16 with Row-Level Security (not schema-per-tenant) | ADR-0001, ADR-0012 |
| Tenant scope | One connection + tx per request, `SET LOCAL` on begin, commit before response | ADR-0090 |
| Search | Elasticsearch shared index + per-tenant routing + filtered aliases | ADR-0002, ADR-0013 |
| Messaging | Apache Kafka via Strimzi on K8s, KRaft mode (not RabbitMQ/Redpanda) | ADR-0003 |
| Kafka outbound | Wolverine with transactional outbox (EF Core + PostgreSQL) | ADR-0080 |
| Kafka inbound | KafkaFlow — per-key parallel-within-partition workers | ADR-0081 |
| CQRS mediator | Wolverine (mandatory pattern); MediatR and MassTransit **not** used | ADR-0028, ADR-0080 |
| Blob storage | S3 abstraction with MinIO default | ADR-0004 |
| Identity | KeyCloak self-hosted, OIDC/JWT | ADR-0006, ADR-0007 |
| API style | REST (not GraphQL), OpenAPI 3.x auto-generated | ADR-0029, ADR-0034 |
| Webhooks | HMAC-SHA256 + retry + DLQ + idempotency keys | ADR-0033 |
| Agent transport | HTTPS polling (not gRPC streaming) — proxy-friendly | ADR-0042 |
| Agent binary | .NET AOT, long-lived token with dual-token rotation | ADR-0041, ADR-0042 |
| CLI | `dotnet tool` + standalone AOT binaries | ADR-0046 |
| Health checks | Three K8s probes via ASP.NET Core HealthChecks | ADR-0060 |
| Entity model | 9 fixed types + JSONB `custom_attributes` (MVP); 10th Custom Entity in Phase 2 | ADR-0064 |
| Pricing | Four tiers: Free / Starter / Pro / Enterprise | ADR-0061 |
| Encryption | Narrow scope — OAuth tokens (column-level) + TLS 1.2+; **mTLS not used** | ADR-0077 |
| Deployment | Kubernetes, cloud-agnostic (no managed-service lock-in) | ADR-0022 |
| Helm chart | Co-located at `deploy/helm/kartova/`, versioned with app, published as OCI to GHCR on release | ADR-0086 |
| DB migrations | Dedicated `Kartova.Migrator` container — K8s Helm pre-upgrade Job or Docker init container; never at app startup | ADR-0085 |
| Local dev | Docker Compose | ADR-0024 |

## Phases (MVP = 0–5)

0. Foundation (E-01) — scaffolding, CI/CD, auth, compliance, observability
1. Core Catalog & Notifications (E-02..E-06, E-06a)
2. Auto-Import (E-07..E-10) — GitHub, Azure DevOps, scorecards
3. Documentation (E-11) — markdown, OpenAPI, AsyncAPI
4. Status Page (E-12)
5. CLI, Policy & Billing (E-13, E-14, E-14a)

Post-MVP: 6 Agent · 7 Intelligence · 8 Analytics · 9 Advanced

## Working agreements

- **Before architectural suggestions:** check ADR keyword index in `docs/architecture/decisions/README.md` — 89 decisions already made
- **Frontend / UI work:** read local mockup first from `docs/ui-screens/{screen}/code.html` + `screen.png` (canonical snapshot, per ADR-0087); escalate to Stitch MCP only when screen is missing locally or user asks for sync. Map Stitch HTML → shadcn/ui components (ADR-0088). Verify with Playwright MCP (**cold-start dev server first** — HMR cache can mask config errors — then navigate → interact → snapshot → check console) before claiming done (ADR-0084).
- **Before adding features:** verify they're not already scoped in `EPICS-AND-STORIES.md`; map each feature to its owning module (ADR-0082)
- **Tenant scope & DB access:** All tenant-scoped DB work runs inside `ITenantScope` (one open connection + transaction per request, `SET LOCAL app.current_tenant_id` on `Begin`). Register module DbContexts via `AddModuleDbContext<T>` — never raw `AddDbContext` for tenant-owned data. Transport adapters (ASP.NET endpoint filter, Wolverine/Kafka middleware) call `Begin`/`CommitAsync` — handlers never touch the scope. See ADR-0090.
- **Cross-module interactions:** only via Wolverine `IMessageBus` (in-process) or Kafka events; never direct references to other modules' Domain/Application/Infrastructure
- **List endpoints & list screens:** every new list endpoint exposes `sortBy` / `sortOrder` / `cursor` / `limit` and returns `CursorPage<T>` (ADR-0095). Every new list screen wires `useCursorList` + `useListUrlState` + `<DataTable>`. Treat as part of "first cut" — not a follow-up phase. Bounded lists may return flat arrays only when decorated with `[BoundedListResult]` + inline justification.
- **When proposing new ADRs:** preview decision before saving (user reviews first)
- **Dates in memory/docs:** absolute (convert "Thursday" → `2026-03-05`)
- **Implementation work:** Superpowers workflow — `superpowers:brainstorming` → `docs/superpowers/specs/YYYY-MM-DD-*-design.md`, then `superpowers:writing-plans` → `docs/superpowers/plans/YYYY-MM-DD-*-plan.md`, then `superpowers:executing-plans` (ticks checkboxes in-place). Roadmap/scope lives in `docs/product/` (EPICS-AND-STORIES.md, CHECKLIST.md, phases/). **GSD is not used** — existing product docs already cover milestone/phase-level tracking.
- **Per slice:** scope one vertical slice end-to-end (walking skeleton → auth → first CRUD → CI/CD+helm → compliance). After executing a plan, update `docs/product/CHECKLIST.md` to reflect completed stories.
- **Compliance:** GDPR + MiFID II from day one — not bolted on later (see E-01.F-05)
- **Definition of Done:** An implementation is "complete" / "finished" / "ready to merge" only when ALL of the following are green and can be cited by command + output:
  1. Full solution build with `TreatWarningsAsErrors=true` (0 warnings, 0 errors).
  2. Per-task subagent reviews (spec-compliance + code-quality) executed — **never skipped on grounds of "trivial"**; review is cheap and its purpose is to force the pause that catches rationalization.
  3. `superpowers:requesting-code-review` invoked at slice boundary against the **full branch diff** with spec + plan as context — catches cross-task design issues the per-task loop can't see (e.g., interaction between filter defined in Task N and wiring in Task M).
  4. Full test suite green: unit + architecture + integration (Testcontainers).
  5. For any slice that wires HTTP / auth / DB / middleware / pipeline: at least one `docker compose up` + real HTTP happy-path + one negative-path, output captured and confirmed. Unit + architecture tests alone are the wrong layer of evidence for these slices — they won't catch filter-vs-binding order, JWT issuer/audience, `SET LOCAL` semantics, or Dockerfile restore gaps.

  Until all five are green, the honest status is **"implementation staged, verification pending"** — never "slice N complete". If a step cannot be run locally (e.g., Docker unavailable on this machine), say so explicitly and flag as *pending user verification*, never imply completion. A Stop hook at `~/.claude/hooks/dod-check.js` blocks turns that assert completion without citing verification evidence.
