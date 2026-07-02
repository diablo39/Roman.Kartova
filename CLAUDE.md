# Kartova — Project Guide for Claude

**Product:** Kartova — SaaS service catalog + developer portal (Backstage + Compass + Statuspage hybrid)
**Owner:** Solo developer (Roman Głogowski), AI-assisted
**Stack:** .NET 10 (LTS) / ASP.NET Core + EF Core · Wolverine (CQRS mediator + outbound + outbox) · KafkaFlow (inbound Kafka consumers) · React + TypeScript · PostgreSQL 18 (RLS) · Elasticsearch · Apache Kafka (Strimzi/KRaft) · MinIO (S3) · KeyCloak (OIDC/JWT) · Kubernetes


# Tool selection (C# code intelligence + built-ins)

For **C# navigation, impact analysis, and refactor planning**, prefer the **roslyn-codelens** MCP tools (`find_callers`, `find_references`, `find_implementations`, `get_type_hierarchy`, `search_symbols`, `analyze_method`, `analyze_change_impact`, `find_unused_symbols`) — compiler-accurate, beats grep on correctness. The active solution `Kartova.slnx` is auto-loaded. Reach for them the moment "who calls / who references / where is this implemented / what does changing this break" matters — **before** extending a hot method, renaming a shared symbol, or scoping a refactor. Never infer a symbol's caller/consumer set from one file or a grep; confirm it with `find_references` / `find_callers`.

The built-in **`LSP`** tool (Roslyn language server) is the other compiler-accurate option for C# — `goToDefinition`, `findReferences`, `hover`, `documentSymbol`, `workspaceSymbol`, `goToImplementation`, and call-hierarchy (`prepareCallHierarchy` / `incomingCalls` / `outgoingCalls`). Use it for precise position-based lookups (definition/references/hover at a specific line:char) and call graphs; prefer roslyn-codelens for higher-level impact analysis (`analyze_change_impact`, `find_unused_symbols`). Either beats grep for "who calls / where is this defined / where is this implemented" on C#.

Built-in `Read`/`Glob`/`Grep` handle reading a few symbols, regex discovery, and one-off lookups. Built-in `Edit`/`Write` are the edit path for **all** files, code and non-code. TypeScript/React have no dedicated code-intelligence MCP in this repo — use Grep/Read for discovery and Edit/Write for changes.

## Self-check

Before a navigation, impact-analysis, or refactor step on **C#**, ask: "Would roslyn-codelens (`find_callers` / `find_references` / `analyze_change_impact`) beat grep+read here?" If yes, use it. Routine reads and small edits don't need this check.

# Project defaults

General engineering judgment — understand before changing, minimal scope, confirm destructive/outward-facing actions, batch independent tool calls, ask only when ambiguity changes the work — follows the harness defaults and is **not restated here**. Project-specific only:

- Never create `*.md` / README files unless explicitly asked.
- UI/frontend changes you can't verify in a browser: say so, don't claim success.

## Where to find things

| Topic | File |
|-------|------|
| Product requirements (source of truth) | [docs/product/PRODUCT-REQUIREMENTS.md](docs/product/PRODUCT-REQUIREMENTS.md) |
| Backlog index (30 epics, 73 features, 209 stories) | [docs/product/EPICS-AND-STORIES.md](docs/product/EPICS-AND-STORIES.md) |
| Progress checklist | [docs/product/CHECKLIST.md](docs/product/CHECKLIST.md) |
| Phase files (0–9) | `docs/product/phases/phase-N-*.md` |
| ADR library + keyword index | [docs/architecture/decisions/README.md](docs/architecture/decisions/README.md) |
| Individual ADRs | `docs/architecture/decisions/ADR-NNNN-*.md` |
| Design system (tokens, nav specs) | [docs/design/DESIGN.md](docs/design/DESIGN.md) |
| Google Stitch prompts | [docs/design/STITCH-PROMPTS.md](docs/design/STITCH-PROMPTS.md) |
| UI mockups (Stitch output, canonical) | `docs/ui-screens/{screen-name}/{code.html, screen.png}` |
| Per-slice implementation specs & plans | `docs/superpowers/specs/YYYY-MM-DD-*-design.md` + `docs/superpowers/plans/YYYY-MM-DD-*-plan.md` |
| Per-slice verification proof (DoD ledger + reviews + evidence) | `docs/superpowers/verification/{date}-{topic}/` (entry point: `dod.md`) |
| Testing strategy (tiers, real-seam rule, fixtures) | [docs/TESTING-STRATEGY.md](docs/TESTING-STRATEGY.md) |

## Conventions

- Epic = `E-XX` · Feature = `E-XX.F-YY` · Story = `E-XX.F-YY.S-ZZ`
- ADR = `ADR-NNNN` (Michael Nygard template)
- Dates: always absolute (`2026-04-21`), never relative
- Windows shell: `cmd //c` (double slash — MSYS path-translation workaround) or PowerShell wrappers for `dotnet` commands in Git Bash. Git Bash lacks `grep -P` (use `-E` or `Select-String`)
- Solution file: `Kartova.slnx` (XML format, .NET 10 default — see ADR-0082). Classic `.sln` not used
- Coverage exclusion: `*.Contracts` types, every `*Dto`/`*Request`/`*Response`, design-time `IDesignTimeDbContextFactory<>` factories, `IModule` composition classes (`*Module.cs`), and test infrastructure MUST carry `[ExcludeFromCodeCoverage]`. Enforced by `tests/Kartova.ArchitectureTests/ContractsCoverageRules.cs` (fails the arch suite if missing).

## Architectural guardrails

Quick-reference cache of ADR decisions, loaded every turn. **The cited ADR is authoritative — if a line here conflicts with its ADR, the ADR wins and this line is stale; fix it.** When an ADR is superseded, grep this file for the ADR number. For everything else, see [docs/architecture/decisions/README.md](docs/architecture/decisions/README.md).

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
- **Testing:** five-tier pyramid — architecture (NetArchTest, mandatory CI gate) + unit + integration (Testcontainers) + contract (Pact) + E2E (Playwright); MSTest v4 framework + native asserts + NSubstitute (ADR-0097, supersedes ADR-0083)

## Phases

MVP = phases 0–5 (Foundation → Core Catalog → Auto-Import → Docs → Status Page → CLI/Policy/Billing). Post-MVP = 6–9 (Agent · Intelligence · Analytics · Advanced). Per-phase scope + current status: [docs/product/CHECKLIST.md](docs/product/CHECKLIST.md) and `docs/product/phases/phase-N-*.md`.

## Working agreements

- **Before architectural suggestions:** check ADR keyword index in `docs/architecture/decisions/README.md`
- **C# code intelligence:** Use the `roslyn-codelens` MCP tools (`find_callers`, `find_references`, `find_implementations`, `get_type_hierarchy`, `search_symbols`, `analyze_method`, `analyze_change_impact`, `find_unused_symbols`, etc.) as the default for navigation, impact analysis, and refactor planning in C# code — before extending a hot method, renaming a constant, modifying a domain method, or scoping a refactor. Grep/Read remain fine for one-off lookups, but the moment "who calls / who references / where is this implemented" matters, prefer codelens. The active solution is `Kartova.slnx` (auto-loaded).
- **Frontend / UI work:** read local mockup first from `docs/ui-screens/{screen}/code.html` + `screen.png` (canonical snapshot, per ADR-0087); escalate to Stitch MCP only when screen is missing locally or user asks for sync. Map Stitch HTML → Untitled UI / react-aria-components (ADR-0094, supersedes the shadcn/ui mapping in ADR-0088). Verify with Playwright MCP (**cold-start dev server first** — HMR cache can mask config errors — then navigate → interact → snapshot → check console) before claiming done (ADR-0084).
- **Before adding features:** verify they're not already scoped in `EPICS-AND-STORIES.md`; map each feature to its owning module (ADR-0082)
- **Tenant scope & DB access:** All tenant-scoped DB work runs inside `ITenantScope` (one open connection + transaction per request, `SET LOCAL app.current_tenant_id` on `Begin`). Register module DbContexts via `AddModuleDbContext<T>` — never raw `AddDbContext` for tenant-owned data. Transport adapters (ASP.NET endpoint filter, Wolverine/Kafka middleware) call `Begin`/`CommitAsync` — handlers never touch the scope. See ADR-0090.
- **Cross-module interactions:** only via Wolverine `IMessageBus` (in-process) or Kafka events; never direct references to other modules' Domain/Application/Infrastructure
- **List endpoints & list screens:** every new list endpoint exposes `sortBy` / `sortOrder` / `cursor` / `limit` and returns `CursorPage<T>` (ADR-0095). Every new list screen wires `useCursorList` + `useListUrlState` + `<DataTable>`. Treat as part of "first cut" — not a follow-up phase. Bounded lists may return flat arrays only when decorated with `[BoundedListResult]` + inline justification.
- **List surface — columns / sort / filters (ADR-0107, ADR-0095):** when designing a **new** list slice, present a per-field **surface proposal** (each field: show as column? · sortable? · filterable + control) and confirm it with the human *before finalizing the design*. That confirmed table produces the ADR-0095 `sortBy` allowlist and the ADR-0107 **Filter Proposal** (each filter field: implement-now / defer / none-needed; deferral explicit, never silent). Mirror filter outcomes into `docs/design/list-filter-registry.md` (canonical per-list record). Built filters render through the standard `<FilterBar>` / `useListFilters` and feed the ADR-0095 `f` map — whose *wire format* lives in ADR-0095; the *mandate + UI* live in ADR-0107.
- **New field → revisit list surface (field-addition trigger):** any slice adding a new queryable / user-facing field to an entity that **already has a list screen** MUST, for each such list, record a decision on all three axes — **column? · sort? · filter?** Each defaults to "reconsider"; opting an axis out is the explicit decision, noted in the registry row. Design/review gate, not an automated check.
- **When proposing new ADRs:** preview decision before saving (user reviews first)
- **Implementation work:** Superpowers workflow — `superpowers:brainstorming` → `docs/superpowers/specs/YYYY-MM-DD-*-design.md`, then `superpowers:writing-plans` → `docs/superpowers/plans/YYYY-MM-DD-*-plan.md`, then `superpowers:executing-plans` (ticks checkboxes in-place). Roadmap/scope lives in `docs/product/` (EPICS-AND-STORIES.md, CHECKLIST.md, phases/). **GSD is not used** — existing product docs already cover milestone/phase-level tracking.
- **Slice specs & test artifacts:** any slice wiring HTTP/auth/DB/middleware MUST, in its design's *Testing Strategy* section, apply [docs/TESTING-STRATEGY.md](docs/TESTING-STRATEGY.md) and name the gate-5 artifacts as deliverables (real-seam integration tests — `KartovaApiFixtureBase`, real Postgres/RLS + real JWT validation; ≥1 happy + ≥1 negative case; container-build coverage for any Dockerfile/`COPY` change). `writing-plans` then emits one task per named artifact (its spec-coverage self-review enforces this). Spec/plan DoD sections **link** CLAUDE.md's nine gates — never restate them.
- **Per slice:** scope one vertical slice end-to-end (walking skeleton → auth → first CRUD → CI/CD+helm → compliance). After executing a plan, update `docs/product/CHECKLIST.md` to reflect completed stories.
- **Slice size:** target **~400 lines of production business code**, hard ceiling **~800** (exclude tests, generated code, EF migrations, DTOs/Contracts). A slice over the ceiling MUST be decomposed during `brainstorming` into sequential sub-slices, each shippable on its own.
- **Compliance:** GDPR-only from day one — not bolted on later (see E-01.F-05). MiFID II and a later-considered NIS2 pivot were both dropped (ADR-0106); there is no per-tenant compliance flag or 5-year retention tier
- **Definition of Done:** "complete" / "finished" / "ready to merge" only when the **eight always-blocking gates** are green and citable by command + output. Gate 6 (mutation) is **conditional** — blocking when the diff touches Domain/Application logic, else should-do. Order is fail-fast: cheap checks first, code-mutating gates before the final re-verify.
  1. Full solution build, `TreatWarningsAsErrors=true` (0 warnings, 0 errors).
  2. Per-task subagent reviews (spec-compliance + code-quality), interleaved during dev. **Never skipped as "trivial".**
  3. Full test suite green: unit + architecture + integration (Testcontainers). Wiring slices (HTTP/auth/DB/middleware) MUST hit the **real seam**. Real `JwtBearer`/KeyCloak + real Postgres/RLS — never the fake auth handler or mocked DbContext. Covers filter-vs-binding order, JWT issuer/audience, `SET LOCAL`.
  4. **Container build green:** the `images` CI job (`docker compose build`). It catches Dockerfile/restore gaps — the one seam tests can't reach. Manual `docker compose up` is smoke, not evidence. Any bug it finds becomes a regression test.
  5. `/simplify` against the branch diff. Should-fix items (reuse/quality/efficiency) addressed or skipped with a reason.
  6. Mutation loop on changed files: `/misc:mutation-sentinel` → `/misc:test-generator`. **Conditional** — blocking when the diff touches Domain/Application logic; otherwise should-do (too heavy every slice): run when practical or skip with a noted reason. Target ≥80% (`stryker-config.json`); document survivors.
  7. `/superpowers:requesting-code-review` at slice boundary, against the **full branch diff** (spec + plan as context). Runs on green, final code — so it catches design issues, not test failures.
  8. `/pr-review-toolkit:review-pr`.
  9. `/deep-review` against the branch diff (spec/plan/ADRs/tests). Blocking + Should-fix addressed, nits triaged.

  **DoD ledger (queryable status):** each slice maintains a DoD ledger at `docs/superpowers/verification/<date>-<topic>/dod.md` — copy `docs/superpowers/templates/dod-ledger-template.md` at slice start and update each gate's row the moment that gate runs (not just at close). Reviews, deep-review reports, and raw evidence (screenshots/logs) live as siblings in the same `verification/<date>-<topic>/` folder; `dod.md` is the index. A "what's the DoD status?" question is answered by reading that file's summary table. Completion claims MUST cite the ledger path — the `.claude/hooks/dod-check.js` stop hook blocks claims that don't. Alongside `dod.md`, each slice keeps `gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`) — a machine-readable per-finding log (gate slug · severity · `real`/`delusion` verdict) so gate effectiveness is queryable across slices; `dod.md` records each gate's *status*, `gate-findings.yaml` records what it *found*.

  **Terminal re-verify:** gates 5–9 may apply fixes that invalidate the green claims from 1 + 3. So after gate 9, re-run build + full suite and confirm green. Until the eight blocking gates pass, the honest status is **"implementation staged, verification pending"** — never "slice N complete". Steps that can't run locally (e.g., no Docker) → flag *pending user verification*. Stop hook `.claude/hooks/dod-check.js` blocks completion claims that lack evidence.

  **Pre-push CI mirror (fast feedback):** before `git push` / opening a PR, run `scripts/ci-local.sh` (mirrors all `.github/workflows/ci.yml` jobs in CI's `--configuration Release`). Gates 1/3/4 run Debug per-gate; this is the one place the **Release** build+test, the web image, and helm/stryker all run as CI will. Run it (or the relevant subset, e.g. `scripts/ci-local.sh backend`) and confirm green before push — catch CI failures locally, not on the PR. Caveat: it runs on the host, not the ubuntu runner, and **cannot catch nondeterministic flakes** (a CI failure that reproduces neither locally nor on re-run is a flaky test → fix determinism, don't re-push blindly).
