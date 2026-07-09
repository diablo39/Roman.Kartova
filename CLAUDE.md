# Kartova — Project Guide for Claude

**Product:** Kartova — SaaS service catalog + developer portal (Backstage + Compass + Statuspage hybrid)
**Owner:** Solo developer (Roman Głogowski), AI-assisted
**Stack:** .NET 10 (LTS) / ASP.NET Core + EF Core · Wolverine (CQRS mediator + outbound + outbox) · KafkaFlow (inbound Kafka consumers) · React + TypeScript · PostgreSQL 18 (RLS) · Elasticsearch · Apache Kafka (Strimzi/KRaft) · MinIO (S3) · KeyCloak (OIDC/JWT) · Kubernetes


# Tool selection (C# code intelligence + built-ins)

For **C# navigation, impact analysis, and refactor planning**, prefer the **roslyn-codelens** MCP tools (`find_callers`, `find_references`, `find_implementations`, `get_type_hierarchy`, `search_symbols`, `analyze_method`, `analyze_change_impact`, `find_unused_symbols`) — compiler-accurate, beats grep on correctness. The active solution `Kartova.slnx` is auto-loaded. Reach for them the moment "who calls / who references / where is this implemented / what does changing this break" matters — **before** extending a hot method, renaming a shared symbol, or scoping a refactor. Never infer a symbol's caller/consumer set from one file or a grep; confirm it with `find_references` / `find_callers`.

> **Carve-out — `const` members: use grep, not codelens.** `find_references`/`find_callers` reliably resolve **methods, types, interfaces, and call-hierarchy** (e.g. `ForRole` → all 8 refs incl. tests). They **materially under-report C# `const` members** — because `public const` values are inlined at compile time, codelens returns ~1 of many usages (verified: `KartovaPermissions.CatalogServicesRegister`/`CatalogApisRegister` → 1 of ~7; misses the `All` array, role map, arch tests, matrix; persists after `rebuild_solution`). For **const / permission-string / enum-literal** symbols, **use `Grep`** for the blast radius. Codelens is also blind outside its loaded solution graph and to the entire non-C# stack (TS/JSON) — for cross-stack couplings like the permission 5-sync, grep + domain knowledge, not codelens. A `"project":""` field in results signals a stale index (new commits) — `rebuild_solution` refreshes it (but not the const-miss).

For position-based lookups (definition/references at a specific line:char) and call graphs, roslyn-codelens also offers `go_to_definition`, `get_call_graph`, `get_type_hierarchy`, and `find_implementations` — all compiler-accurate and preferable to grep for "who calls / where is this defined / where is this implemented" on C#.

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
| Backlog index (31 epics, 83 features, 224 stories) | [docs/product/EPICS-AND-STORIES.md](docs/product/EPICS-AND-STORIES.md) |
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
- Endpoint-URL validation: require a non-empty `Uri.Authority`, not just `UriKind.Absolute`. Rooted paths (`/v1/orders`) parse as absolute on Linux CI but not Windows — so `UriKind.Absolute`-only tests pass locally and fail CI. Use `Uri.TryCreate(url, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Authority)`.

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
- **API entity model:** API is first-class (`EntityKind.Api`), one unified aggregate keyed by `Style` (`Rest`/`Grpc`/`GraphQL`/`AsyncApi`); async detail lives in the stored spec doc, not columns. Spec documents stored as `text` in `catalog_api_specs` (RLS, 1:1, ADR-0112). Provider, instance, and consumer links are all **relationship edges** — `provides-api-for` ({Application, Service} → Api), `instance-of` (Service → Application), `consumes-api-from` ({Application, Service} → Api). **No** provider/instance FK columns. Exposure (`exposes`) and service↔service `depends-on` **derive** over edges (deferred: FU-B). `ServiceEndpoint` = labeled address (protocol dropped → `Api.Style`) (ADR-0111 **amended 2026-07-07 unified-entity + spec-storage**, amends ADR-0068)
- **Agent transport:** HTTPS polling — gRPC streaming **not** used (ADR-0042)
- **Frontend UI stack:** Untitled UI free-tier (react-aria-components + Tailwind v4) + @untitledui/icons (ADR-0094)
- **Testing:** five-tier pyramid — architecture (NetArchTest, mandatory CI gate) + unit + integration (Testcontainers) + contract (Pact) + E2E (Playwright); MSTest v4 framework + native asserts + NSubstitute (ADR-0097, supersedes ADR-0083)

## Phases

MVP = phases 0–5 (Foundation → Core Catalog → Auto-Import → Docs → Status Page → CLI/Policy/Billing). Post-MVP = 6–9 (Agent · Intelligence · Analytics · Advanced). Per-phase scope + current status: [docs/product/CHECKLIST.md](docs/product/CHECKLIST.md) and `docs/product/phases/phase-N-*.md`.

## Working agreements

- **Before architectural suggestions:** check ADR keyword index in `docs/architecture/decisions/README.md`
- **C# code intelligence:** Use the `roslyn-codelens` MCP tools (`find_callers`, `find_references`, `find_implementations`, `get_type_hierarchy`, `search_symbols`, `analyze_method`, `analyze_change_impact`, `find_unused_symbols`, etc.) as the default for navigation, impact analysis, and refactor planning in C# code — before extending a hot method, renaming a constant, modifying a domain method, or scoping a refactor. Grep/Read remain fine for one-off lookups, but the moment "who calls / who references / where is this implemented" matters, prefer codelens. The active solution is `Kartova.slnx` (auto-loaded).
- **Frontend / UI work:** read local mockup first from `docs/ui-screens/{screen}/code.html` + `screen.png` (canonical snapshot, per ADR-0087); escalate to Stitch MCP only when screen is missing locally or user asks for sync. Map Stitch HTML → Untitled UI / react-aria-components (ADR-0094, supersedes the shadcn/ui mapping in ADR-0088). Verify with Playwright MCP (**cold-start dev server first** — HMR cache can mask config errors — then navigate → interact → snapshot → check console) before claiming done (ADR-0084).
- **react-aria `<Table>` needs exactly one `isRowHeader` column** (`@/components/application/table`) — mark the identifying `Table.Head` (`id="displayName" isRowHeader`). Missing it throws at `TableCollection.updateColumns`; React recovers on a light render, but a heavier re-render (opening a modal/overlay) **blank-pages the whole screen**. jsdom recovers silently so unit tests miss it — assert `getAllByRole("rowheader").length > 0` **and** open dialogs in a real browser (ADR-0084).
- **Before adding features:** verify they're not already scoped in `EPICS-AND-STORIES.md`; map each feature to its owning module (ADR-0082)
- **Tenant scope & DB access:** All tenant-scoped DB work runs inside `ITenantScope` (one open connection + transaction per request, `SET LOCAL app.current_tenant_id` on `Begin`). Register module DbContexts via `AddModuleDbContext<T>` — never raw `AddDbContext` for tenant-owned data. Transport adapters (ASP.NET endpoint filter, Wolverine/Kafka middleware) call `Begin`/`CommitAsync` — handlers never touch the scope. See ADR-0090.
- **Cross-module interactions:** only via Wolverine `IMessageBus` (in-process) or Kafka events; never direct references to other modules' Domain/Application/Infrastructure
- **Adding a `KartovaPermission` = 5 synced touchpoints.** Backend arch tests guard only C#↔snapshot, so a missed frontend edit passes Backend CI but **fails the Frontend job** (drift thrown at `permissions.ts` import). (1) `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` — `const` + add to `All`; (2) `KartovaRolePermissions.cs` — map to roles; (3) `web/src/shared/auth/permissions.snapshot.json`; (4) `web/src/shared/auth/permissions.ts` — TS constant; (5) `web/src/shared/auth/__tests__/usePermissions.test.tsx` — OrgAdmin full-set mock. Grep the blast radius — codelens under-reports `const` refs (see carve-out).
- **List endpoints & list screens:** every new list endpoint exposes `sortBy` / `sortOrder` / `cursor` / `limit` and returns `CursorPage<T>` (ADR-0095). Every new list screen wires `useCursorList` + `useListUrlState` + `<DataTable>`. Treat as part of "first cut" — not a follow-up phase. Bounded lists may return flat arrays only when decorated with `[BoundedListResult]` + inline justification. **Default sort = `displayName asc`** for name-bearing lists — standardized across Teams / Services / Applications (screen + endpoint agree; `createdAt` stays in the allowlist). Lists with no own `displayName` (e.g. relationships) document an alternative default in `docs/design/list-filter-registry.md`.
- **List surface — columns / sort / filters (ADR-0107, ADR-0095):** when designing a **new** list slice, present a per-field **surface proposal** (each field: show as column? · sortable? · filterable + control) and confirm it with the human *before finalizing the design*. That confirmed table produces the ADR-0095 `sortBy` allowlist and the ADR-0107 **Filter Proposal** (each filter field: implement-now / defer / none-needed; deferral explicit, never silent). Mirror filter outcomes into `docs/design/list-filter-registry.md` (canonical per-list record). Built filters render through the standard `<FilterBar>` / `useListFilters` and feed the ADR-0095 `f` map — whose *wire format* lives in ADR-0095; the *mandate + UI* live in ADR-0107.
- **New field → revisit list surface (field-addition trigger):** any slice adding a new queryable / user-facing field to an entity that **already has a list screen** MUST, for each such list, record a decision on all three axes — **column? · sort? · filter?** Each defaults to "reconsider"; opting an axis out is the explicit decision, noted in the registry row. Design/review gate, not an automated check.
- **When proposing new ADRs:** preview decision before saving (user reviews first)
- **Implementation work:** Superpowers workflow — `superpowers:brainstorming` → `docs/superpowers/specs/YYYY-MM-DD-*-design.md`, then `superpowers:writing-plans` → `docs/superpowers/plans/YYYY-MM-DD-*-plan.md`, then `superpowers:executing-plans` (ticks checkboxes in-place). Roadmap/scope lives in `docs/product/` (EPICS-AND-STORIES.md, CHECKLIST.md, phases/). **GSD is not used** — existing product docs already cover milestone/phase-level tracking.
- **Subagent-driven dev — verify the commit landed.** After a subagent task, confirm its commit is an ancestor of branch HEAD (`git merge-base --is-ancestor <sha> HEAD`) and **re-run the suite yourself** — never trust the agent's "N/N passing". A suspiciously low tool-use count on a heavy task is a non-persistence signal: worktree-isolated commits can orphan (parent = HEAD but the branch ref never advanced; `git fsck --dangling` surfaces them, `git merge --ff-only <sha>` recovers).
- **`superpowers:writing-plans` — C# impact analysis (codelens, enforceable):** every plan MUST include an `## Impact Analysis (codelens)` section — copy the skeleton from `docs/superpowers/templates/plan-impact-analysis.md` right after `## Global Constraints`. When the plan changes an **existing** C# symbol's signature or behavior (domain/application method, shared const, interface, public API), that section's blast-radius reasoning MUST be grounded in `roslyn-codelens` (`find_callers` / `find_references` / `analyze_change_impact` — `find_implementations` / `get_type_hierarchy` for interface/base changes), citing what it found (caller/reference count + notable call sites) and confirming every caller is covered by a task — not a grep guess. New-code-only or non-C# plans keep the heading with a one-line `N/A — <reason>` (never delete it silently). A grep-only or missing section is an **incomplete plan**, flagged at gate 2 / gate 7 / gate 9. This is the highest-value, most-enforceable codelens spot; the general "prefer codelens" guidance above still applies elsewhere but is advisory.
- **Slice specs & test artifacts:** any slice wiring HTTP/auth/DB/middleware MUST, in its design's *Testing Strategy* section, apply [docs/TESTING-STRATEGY.md](docs/TESTING-STRATEGY.md) and name the gate-5 artifacts as deliverables (real-seam integration tests — `KartovaApiFixtureBase`, real Postgres/RLS + real JWT validation; ≥1 happy + ≥1 negative case; container-build coverage for any Dockerfile/`COPY` change). `writing-plans` then emits one task per named artifact (its spec-coverage self-review enforces this). Spec/plan DoD sections **link** CLAUDE.md's eleven gates — never restate them.
- **Per slice:** scope one vertical slice end-to-end (walking skeleton → auth → first CRUD → CI/CD+helm → compliance). After executing a plan, update `docs/product/CHECKLIST.md` to reflect completed stories.
- **Slice size:** target **~400 lines of production business code**, hard ceiling **~800** (exclude tests, generated code, EF migrations, DTOs/Contracts). A slice over the ceiling MUST be decomposed during `brainstorming` into sequential sub-slices, each shippable on its own.
- **Compliance:** GDPR-only from day one — not bolted on later (see E-01.F-05). MiFID II and a later-considered NIS2 pivot were both dropped (ADR-0106); there is no per-tenant compliance flag or 5-year retention tier
- **Definition of Done:** "complete" / "finished" / "ready to merge" only when the **ten always-blocking gates** are green and citable by command + output. Gate 6 (mutation) is **conditional** — blocking when the diff touches Domain/Application logic, else should-do. Order is fail-fast: cheap checks first, code-mutating gates before the final re-verify, and the running-system + CI gates (10–11) last.
  - **Every gate runs for real — no folding.** Never mark a gate "covered by" another (e.g. gate 8 `review-pr` folded into 7/9) or skip it as redundant — the gates are different lenses and catch different defect classes (running gate 8 for real once caught two findings gates 7+9 both missed). A gate that genuinely doesn't apply is marked **N/A with a reason** (e.g. Playwright on a backend-only slice); an owner-waived conditional gate is recorded as a **waiver, not green**. After any gate applies fixes, re-run build + full suite on the **final** commit before claiming green.
  1. Full solution build, `TreatWarningsAsErrors=true` (0 warnings, 0 errors).
  2. Per-task subagent reviews (spec-compliance + code-quality), interleaved during dev. **Never skipped as "trivial".**
  3. Full test suite green: unit + architecture + integration (Testcontainers). Wiring slices (HTTP/auth/DB/middleware) MUST hit the **real seam**. Real `JwtBearer`/KeyCloak + real Postgres/RLS — never the fake auth handler or mocked DbContext. Covers filter-vs-binding order, JWT issuer/audience, `SET LOCAL`.
  4. **Container build green:** the `images` CI job (`docker compose build`). It catches Dockerfile/restore gaps — the one seam tests can't reach. Manual `docker compose up` is smoke, not evidence. Any bug it finds becomes a regression test.
  5. `/simplify` against the branch diff. Should-fix items (reuse/quality/efficiency) addressed or skipped with a reason.
  6. Mutation loop on changed files: `/misc:mutation-sentinel` → `/misc:test-generator`. **Conditional** — blocking when the diff touches Domain/Application logic; otherwise should-do (too heavy every slice): run when practical or skip with a noted reason. Target ≥80% (`stryker-config.json`); document survivors.
  7. `/superpowers:requesting-code-review` at slice boundary, against the **full branch diff** (spec + plan as context). Runs on green, final code — so it catches design issues, not test failures.
  8. `/pr-review-toolkit:review-pr`.
  9. `/deep-review` against the branch diff (spec/plan/ADRs/tests). Blocking + Should-fix addressed, nits triaged.
  10. **Visual / API verification — observe running system (exploratory + data-shape).** Drive the change on the real stack to surface what automated tests structurally cannot: drifted/legacy production data, unknown-unknowns, first-time visual surfaces. UI slices → cold-start, authenticate, navigate **in-SPA** (ADR-0084), screenshot the changed surface; API slices → exercise the **live** endpoint (real auth + DB), capture the request/response. **Deterministic user-flow regressions belong in the nightly Playwright E2E suite** (`e2e/`), not here — converting a gate-10 finding into an E2E spec is the expected follow-up ("any bug it finds becomes a regression test"). Gate 10 stays a per-slice human/MCP pass and does **not** fold into E2E (different lenses — no-folding rule). Evidence committed under `verification/<date>-<topic>/`. N/A (with reason) only when the diff has no runtime surface (docs / pure refactor).
  11. **CI green on the PR — terminal.** The PR's CI run is green; the runner is the source of truth. `scripts/ci-local.sh` (Release mirror) is the **required pre-push** fast-feedback step, but green-locally ≠ green-on-CI: a CI-only failure is usually a nondeterministic flake → **fix determinism, don't re-push blindly** (see Pre-push CI mirror below).

  **DoD ledger (queryable status):** each slice maintains a DoD ledger at `docs/superpowers/verification/<date>-<topic>/dod.md` — copy `docs/superpowers/templates/dod-ledger-template.md` at slice start and update each gate's row the moment that gate runs (not just at close). Reviews, deep-review reports, and raw evidence (screenshots/logs) live as siblings in the same `verification/<date>-<topic>/` folder; `dod.md` is the index. A "what's the DoD status?" question is answered by reading that file's summary table. Completion claims MUST cite the ledger path — the `.claude/hooks/dod-check.js` stop hook blocks claims that don't. Alongside `dod.md`, each slice keeps `gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`) — a machine-readable per-finding log (gate slug · severity · `real`/`delusion` verdict) so gate effectiveness is queryable across slices; `dod.md` records each gate's *status*, `gate-findings.yaml` records what it *found*.

  **Terminal re-verify:** gates 5–9 may apply fixes that invalidate the green claims from 1 + 3. So after gate 9, re-run build + full suite and confirm green; gate 10 (visual/API) then observes that final build, and gate 11 (CI green) follows the push. Until the ten blocking gates pass, the honest status is **"implementation staged, verification pending"** — never "slice N complete". Steps that can't run locally (e.g., no Docker) → flag *pending user verification*. Stop hook `.claude/hooks/dod-check.js` blocks completion claims that lack evidence.

  **Pre-push CI mirror (fast feedback) — the pre-push half of gate 11:** before `git push` / opening a PR, run `scripts/ci-local.sh` (mirrors all `.github/workflows/ci.yml` jobs in CI's `--configuration Release`). Gates 1/3/4 run Debug per-gate; this is the one place the **Release** build+test, the web image, and helm/stryker all run as CI will. Run it (or the relevant subset, e.g. `scripts/ci-local.sh backend`) and confirm green before push — catch CI failures locally, not on the PR. Caveat: it runs on the host, not the ubuntu runner, and **cannot catch nondeterministic flakes** (a CI failure that reproduces neither locally nor on re-run is a flaky test → fix determinism, don't re-push blindly).
