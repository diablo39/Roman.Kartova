# Spec: Copilot Code Review Instructions for Kartova

**Date:** 2026-05-05
**Author:** Roman Głogowski (with Claude)
**Status:** Draft — pending review

---

## Context & motivation

The repo currently has:
- A rich `CLAUDE.md` covering architecture, ADRs, working agreements, and a 7-step Definition of Done.
- An existing `.github/copilot/skills/deep-review/SKILL.md` — a Copilot **CLI** skill, not instruction files.
- No `.github/copilot-instructions.md` or `.github/instructions/` files for **Copilot code review** or the **Copilot coding agent**.
- A 7-step DoD that already runs `/simplify`, `mutation-sentinel`, per-task subagent reviews, and `superpowers:requesting-code-review` at slice boundary.

This spec adds a fourth review layer: Copilot code review as the *earliest-cheapest gate* on PRs, plus *diversity-of-perspective* cross-check vs. Claude-based reviews. Same instruction files also configure the Copilot coding agent (we'll start using it for some autonomous work).

## Goals (in priority order)

1. **Earliest-cheapest gate**: Copilot reviews each PR/push automatically and catches the cheap-to-fix style/convention things before heavier reviews engage.
2. **Diversity-of-perspective cross-check**: a different model occasionally catches things Claude-based reviews miss. Honest expectation per Morph benchmarks: ~56% precision, ~37% recall on substantive issues.
3. **Coding-agent guidance**: same files instruct any Copilot coding-agent runs to follow the project's conventions (Wolverine, ITenantScope, CursorPage<T>, Untitled UI, etc.).

Non-goals:
- Replace any layer of the DoD (Copilot review is advisory; branch protection still requires human approval).
- Codify the entire CLAUDE.md as instruction-file content (would blow the 4 KB cap; CLAUDE.md remains the workflow / DoD source-of-truth).

## Approach decisions (with rationale)

### D1. Role: earliest-cheapest gate AND diversity-of-perspective (A+B)
- Copilot review runs automatically on every PR / push.
- Catches cheap-to-fix style/convention issues so heavier Claude reviews don't waste cycles.
- Provides cross-model perspective; honest about Copilot's recall limits per the kb-research evidence.

### D2. Structure: manifest + 3 path-scoped files
- `.github/copilot-instructions.md` — thin manifest (<1.2 KB), repo-wide rules + global deny-list.
- `.github/instructions/backend.instructions.md` — `applyTo: "src/**/*.cs"`.
- `.github/instructions/frontend.instructions.md` — `applyTo: "web/src/**/*.{ts,tsx}"`.
- `.github/instructions/tests.instructions.md` — `applyTo: "**/*Tests.cs,**/*Tests/**/*.cs,web/src/**/*.{test,spec}.{ts,tsx}"`.

Rationale: a single 4 KB file would force brutal trimming on either backend or frontend; backend and frontend conventions diverge enough that a split keeps each file focused. Path-scoping via `applyTo` is the only clean OSS exemplar of split (per `github/docs`).

### D3. Content scope: conventions + per-file deny-list
- Conventions: project-specific things a generic .NET / React reviewer wouldn't know.
- Deny-lists: explicit "don't comment on…" suppression for items that other gates already cover (`/simplify`, mutation-sentinel, ESLint, dotnet format, per-task subagent reviews, Stryker).

Rationale: kb-research evidence (Jones tuning report, Childress) — Copilot's biggest noise source is duplicating other gates. Deny-lists materially drop noise.

### D4. Agent scoping: NO `excludeAgent` — both agents read all files
- Coding agent benefits from convention rules ("use Wolverine, not MediatR") when generating new code.
- Review agent uses them to flag violations.
- Deny-list items phrased bidirectionally where possible (e.g., "Don't suggest GraphQL or RTK Query — TanStack Query is the data layer" applies to both agents).

Rationale: We'll use both Copilot review and the Copilot coding agent. Splitting via `excludeAgent` would create two parallel rule files; the duplication cost outweighs the small contamination cost.

### D5. ADR references: stripped from instruction-file bodies, preserved in this spec
- Copilot won't follow ADR links and ADR text isn't fed to the file at review time.
- ADR refs in body cost budget for zero AI-reviewer signal.
- This spec preserves a rule↔ADR traceability map (Appendix A) for human grep when an ADR changes.

### D6. ❌/✅ paired examples: one focused table per file
- kb-research evidence: paired correct-vs-incorrect examples are the highest-leverage rule pattern (GitHub Blog "Mastering instructions files", awesome-copilot generic template, UI5/webcomponents in the wild).
- Each file has one ❌/✅ table at the bottom; rule body uses sparing `❌/✅` only where critical.

### D7. Identifiers Copilot can match in the diff > prose abstractions
- Class names, attribute names, helper names, namespace patterns, Markdown identifiers Copilot can grep in the diff are load-bearing.
- File paths, ADR numbers, prose explanations Copilot can't navigate to are stripped.

---

## File specifications

### F1. `.github/copilot-instructions.md`

```markdown
# Copilot review & coding instructions for Kartova

Path-scoped instructions in `.github/instructions/`:
- `backend.instructions.md` — `src/**/*.cs` (Wolverine, EF Core, tenant scope, modular monolith)
- `frontend.instructions.md` — `web/src/**/*.{ts,tsx}` (React 19, Untitled UI, TanStack Query)
- `tests.instructions.md` — `**/*Tests*.cs` and `web/src/**/*.{test,spec}.{ts,tsx}`

These files serve **both** Copilot code review and Copilot coding agent (no `excludeAgent`). Review is advisory — branch protection + the Definition of Done gate merges.

## Repo-wide rules
- Zero-warning build (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`). Flag changes that introduce warnings even if compilation succeeds.
- Dates in code, comments, commit messages: absolute `YYYY-MM-DD`. Flag relative dates ("Thursday", "next sprint", "recently").
- Solution file is `Kartova.slnx`. Don't suggest reintroducing classic `.sln`.
- Modular monolith — one csproj tree per bounded context. Modules interact only via Wolverine `IMessageBus` or Kafka events; cross-module references go through `*.Contracts` packages only.

## Global deny-list (banned alternatives — don't propose, don't generate)
- REST → GraphQL — banned across the codebase
- Wolverine → MediatR or MassTransit — banned
- PostgreSQL Row-Level Security → schema-per-tenant — banned
- HTTPS polling → gRPC streaming for the agent transport — banned
- Untitled UI (`react-aria-components` + Tailwind v4) → other design systems — banned
- TanStack Query → RTK Query, SWR, GraphQL clients — banned
- `[ExcludeFromCodeCoverage]` on production logic — narrow scope defined in `backend.instructions.md`
```

### F2. `.github/instructions/backend.instructions.md`

```markdown
---
applyTo: "src/**/*.cs"
---

## Don't comment on
- Style, `var`, file-scoped namespaces, `using` order — `.editorconfig` handles
- Missing XML docs; `ConfigureAwait(false)` style; `record`->`class` swaps
- "Add interface for testability" when concrete is fine
- Cyclomatic complexity, function length — `/simplify` handles
- Proposing `[ExcludeFromCodeCoverage]` on production logic
- Handler placement in `*.Infrastructure` (allowed by design)
- Module-layering violations and mutation-score gaps — fitness tests handle

## Mediation & messaging
- Wolverine `IMessageBus` only. Flag `MediatR.IMediator`, `IRequestHandler<>`, `IPublisher`, `MassTransit.*`.
- Cross-module calls go through `IMessageBus` or Kafka events. Flag direct project references between modules.
- Outbound Kafka via Wolverine transactional outbox. Flag direct `IProducer` / raw Confluent client usage.
- Inbound Kafka via KafkaFlow. Flag in-process `IConsumer` patterns.
- Sync HTTP endpoints use direct handler dispatch (shared scope). Flag `IMessageBus.InvokeAsync` inside sync HTTP endpoints; `PublishAsync` stays allowed.

## Tenant scope & data access
- `ITenantScope` lifecycle is owned by `TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter`. Flag handlers calling `Begin`/`CommitAsync` or resolving `ITenantScope` directly.
- Register module DbContexts via `AddModuleDbContext<T>`. Flag raw `services.AddDbContext<T>` for tenant-owned entities.
- `AdminOrganizationDbContext` with `BYPASSRLS` is the only allowed RLS bypass — don't flag.
- App startup must not call `Database.Migrate()` or `EnsureCreated()`. Migrations run only via `Kartova.Migrator`.
- `SET LOCAL app.current_tenant_id` belongs in `TenantScopeBeginMiddleware`. Flag any other path setting tenant scope.

## HTTP & lists
- Routes register via `MapTenantScopedModule(slug)` / `MapAdminModule(slug)`. Flag raw `app.Map*("/api/...")` calls outside these helpers.
- Error responses use `application/problem+json` via `Results.Problem()` / `AddProblemDetails()`. Flag ad-hoc `{ "error": "..." }` shapes.
- New list endpoints accept `sortBy`, `sortOrder`, `cursor`, `limit` and return `CursorPage<T>`. Flag `IEnumerable<T>`/`List<T>` returns from list endpoints. `[BoundedListResult]` requires inline `// reason: ...`.

## Coverage exclusion
- `[ExcludeFromCodeCoverage]` is required on: types in `*.Contracts` assemblies; `*Dto`/`*Request`/`*Response`; `*Module.cs` composition roots; `IDesignTimeDbContextFactory<>` factories. Flag when missing on these; don't propose it elsewhere.

## Secrets, webhooks, observability
- Persist OAuth tokens / secrets only through the AES-256-GCM helper with per-tenant DEK. Flag new columns/properties holding raw token or secret values, plaintext persistence, or hand-rolled crypto.
- Webhook receivers use the shared retry+DLQ+idempotency+rate-limit+HMAC pipeline. Flag bespoke webhook receive code reimplementing any of these.
- Structured `ILogger` only. Flag `Console.Write*`, `Console.Error.*`, and `ILogger.LogXxx` calls in tenant-scoped paths missing `tenant_id` / `correlation_id` properties.

## ❌ / ✅ quick reference
| Concern    | ❌ Bad                          | ✅ Good                          |
|------------|----------------------------------|----------------------------------|
| Mediation  | `IMediator.Send` / `MassTransit` | `IMessageBus`                    |
| DbContext  | `services.AddDbContext<T>`       | `AddModuleDbContext<T>`          |
| List API   | `Task<List<X>>`                  | `Task<CursorPage<X>>`            |
| Errors     | `return BadRequest(new {error})` | `Results.Problem(...)`           |
| Routes     | `app.Map*("/api/x", ...)`        | `MapTenantScopedModule("x")`     |
| Tokens     | `entity.AccessToken = raw`       | `secretCipher.Protect(raw)`      |
```

### F3. `.github/instructions/frontend.instructions.md`

```markdown
---
applyTo: "web/src/**/*.{ts,tsx}"
---

## Don't comment on
- Prettier / ESLint formatting, import order
- Arrow vs function-declaration component style
- `useMemo` / `useCallback` "for perf" without measured evidence
- Component file size — `/simplify` covers it
- Suggestions to swap design system (MUI, Chakra, Mantine, shadcn) — Untitled UI is fixed
- Suggestions to swap data layer (RTK Query, SWR, GraphQL) — TanStack Query is fixed
- Anything under `web/src/generated/` — regenerated by `codegen`, never hand-edited

## Components & UI
- Interactive primitives use `react-aria-components` (Untitled UI). Flag new native `<button>`, `<select>`, `<textarea>`, or non-hidden `<input>` introduced in interactive components. `<input type="hidden">` and form-control inputs already wrapped by an aria primitive are fine.
- Icon-only buttons and links carry `aria-label` (or visually-hidden text). Flag icon-only `<Button>` / `<a>` with no accessible name.
- Icons come from `@untitledui/icons`. Flag `lucide-react`, `react-icons`, `heroicons`, ad-hoc inline `<svg>` as new icon sources.
- Style via Tailwind v4 utilities. Flag inline `style={{...}}` for layout, spacing, color (dynamic-value escape hatch is fine).
- Dark mode toggles the `.dark-mode` class (not Tailwind default `.dark`). Theme switching via `next-themes` `useTheme()`. Flag `dark:` Tailwind variants and direct `document.documentElement.classList` toggling.

## Forms & validation
- Forms use `react-hook-form` + `zod` via `@hookform/resolvers/zod`. Flag `useState`-driven field state in multi-field forms.
- Schemas live next to the component or in a sibling `*.schema.ts`. Flag inline ad-hoc validation chains.

## Data fetching & server state
- Server state via TanStack Query: `useQuery`, `useMutation`, `useInfiniteQuery`. Flag `useState` + `useEffect` + `fetch` for server data.
- HTTP calls go through the generated `openapi-fetch` client (`client.GET`, `client.POST`, etc.). Flag raw `fetch(...)` or `axios` to project endpoints.
- Query consumers handle `isPending` and `isError` (or wrap in a suspense / error boundary). Flag components that destructure `data` without a loading or error branch.
- API errors arrive as RFC 7807 `application/problem+json` (`type`, `title`, `detail`, `traceId`, `errors`). Flag string-matching on error messages or ad-hoc `.catch(e => e.message)` parsing.

## Routing & auth
- Routing uses `react-router-dom` v7 data router APIs (`createBrowserRouter`, loaders, actions). Flag v5 patterns: `<Switch>`, `history` v5 imports, class-component route guards. `useNavigate`, `useParams`, `<Link>`, `<Outlet>` are shared and fine.
- Auth via `react-oidc-context` `useAuth()`. Flag manual JWT decode, raw token reads/writes to `localStorage` / `sessionStorage`, hand-rolled refresh logic.

## Lists & pagination
- New list screens compose `useCursorList` + `useListUrlState` + `<DataTable>`. Cursor pagination is the repo standard.
- List queries use `?sortBy=`, `?sortOrder=asc|desc`, `?cursor=`, `?limit=` (max 200). Flag custom pagination params: `?page=`, `?offset=`, `?perPage=`.

## TypeScript discipline
- Strict mode. Prefer `unknown` + narrowing over `any`. Flag new `any`. Replace `@ts-ignore` with `@ts-expect-error` carrying an inline reason.

## ❌ / ✅ quick reference
| Concern        | ❌                                  | ✅                                          |
|----------------|-------------------------------------|---------------------------------------------|
| Server state   | `useState`+`useEffect`+`fetch`      | `useQuery`                                  |
| HTTP           | `fetch("/api/x")` / `axios`         | `client.GET("/x")`                          |
| Forms          | per-field `useState`                | `useForm` + `zodResolver`                   |
| Auth           | manual JWT decode                   | `useAuth()`                                 |
| Lists          | `useState([])` + manual paging      | `useCursorList` + `<DataTable>`             |
| Icons          | inline `<svg>` / `lucide-react`     | `@untitledui/icons`                         |
| Icon-only btn  | `<Button><Icon/></Button>`          | `<Button aria-label="…">`                   |
| Errors         | `e.message.includes("…")`           | `problem.type === "/errors/…"`              |
| Dark mode      | `className="dark"`                  | `<html class="dark-mode">`                  |
| Type escape    | `any` / `@ts-ignore`                | `unknown` + narrow / `@ts-expect-error` w/ reason |
```

### F4. `.github/instructions/tests.instructions.md`

```markdown
---
applyTo: "**/*Tests.cs,**/*Tests/**/*.cs,web/src/**/*.{test,spec}.{ts,tsx}"
---

## Don't comment on
- Test method naming when the file is internally consistent
- Mocking-library swaps (one lib per assembly)
- "Extract a helper" when existing fixtures already cover the case
- Mutation score, coverage %, `[ExcludeFromCodeCoverage]` placement — other gates own these
- The `Tests` vs `IntegrationTests` project split — intentional
- Missing `await` on `*Async()` calls — CS4014 / async analyzers cover it

## Backend tests (xUnit)
- Use only `[Fact]`, `[Theory]`, `[InlineData]`, `[MemberData]`. Flag NUnit `[Test]`/`[TestCase]`/`[TestFixture]` and MSTest `[TestMethod]`/`[TestClass]`.
- NetArchTest assertions belong in the architecture-tests project. Flag `Types.InAssembly(...)` / `TypesThat()...Should()` calls in unit or integration test files.
- Integration tests run on real Postgres via Testcontainers. Flag `UseInMemoryDatabase`, `UseSqlite("Data Source=:memory:")`, `Microsoft.EntityFrameworkCore.InMemory`, or sqlite shims standing in for tenant data.
- Tenant-scoped tests derive from `KartovaApiFixtureBase` (or `KeycloakContainerFixture` where auth is needed). Flag `new XyzDbContext(...)` constructed inline in a test, or `GetRequiredService<XyzDbContext>()` outside a fixture-provided scope.
- Async test bodies do not call `.Result` or `.Wait()`. Flag both.
- List-endpoint integration tests assert the `CursorPage<T>` envelope (cursor/sortBy/sortOrder/limit). Flag bare `Should().HaveCount(...)` or array-shape assertions on paginated endpoints.
- HTTP error-path integration tests assert `application/problem+json` shape (`type`, `title`, `status`, `traceId`). Flag status-code-only assertions or string-body matching on error responses.

## Frontend tests (Vitest + Testing Library)
- Use Vitest with `@testing-library/react` and `@testing-library/user-event`. Flag `jest.mock`, `jest.fn`, `jest.config.*`, `react-dom/test-utils`, or `enzyme` imports.
- Query by role or accessible name. Flag `container.querySelector(".css-class")`, `getByTestId` when a `getByRole`/`getByLabelText` exists in the same render, and assertions on hook return values when rendered output would suffice.
- Drive interactions through `userEvent` (`await user.click`, `await user.type`). Flag `fireEvent.click`/`fireEvent.change` for user-driven interactions.
- Mock HTTP via `vi.mock` of the typed `openapi-fetch` client or by replacing its transport. Flag `vi.spyOn(globalThis, "fetch")`, raw `vi.fn()` patches against `window.fetch`, hand-built `new Response(...)` mocks, and MSW handlers (MSW is not in the stack).
- Async UI: use `await screen.findBy*` or `await waitFor(...)`. Flag synchronous `getBy*` against state that arrives after a promise.

## Assertions
- Flag a test whose only assertion is `Should().NotBeNull()`, `Should().NotBeEmpty()`, `expect(x).toBeDefined()`, `expect(x).toBeTruthy()`, or `expect(() => fn()).not.toThrow()`.
- Flag value comparisons that check only `.GetType()`, `typeof`, `instanceof`, or property existence without reading the value.

## Quick reference
| Don't                                              | Do                                              |
|----------------------------------------------------|-------------------------------------------------|
| `[Test]` / `[TestMethod]`                          | `[Fact]` / `[Theory]`                           |
| `UseInMemoryDatabase` in integration test          | Testcontainers Postgres via fixture             |
| `new XyzDbContext(opts)` in test body              | resolve via `KartovaApiFixtureBase` scope       |
| `Types.InAssembly(...)` in `*.Tests` project       | move to architecture-tests project              |
| `result.Should().NotBeNull()` alone                | assert specific field values                    |
| `Should().HaveCount(...)` on paginated endpoint    | assert `CursorPage<T>` envelope shape           |
| `StatusCode.Should().Be(400)` only on error path   | assert `problem+json` shape too                 |
| `fireEvent.click(btn)` for user action             | `await user.click(btn)`                         |
| `vi.spyOn(globalThis, "fetch")`                    | `vi.mock` of typed client / transport replace   |
```

---

## Implementation steps

1. Create the four files at the paths in F1–F4.
2. Verify each file's character count is under 3,800 (safety budget; the hard cap is 4,000 for review-time loading).
3. Open Repo Settings → Rules → Rulesets → New branch ruleset:
   - Target: `master`
   - Enforcement: Active
   - "Automatically request Copilot code review": ON
   - "Review new pushes": ON
   - "Review draft pull requests": ON
4. Confirm branch protection on `master` still requires at least one human approver (Copilot's "Comment" review can't satisfy this on its own; this is correct).
5. Open a small calibration PR (e.g. a typo fix or a minimal new test) and verify Copilot review fires across all 4 files' relevant globs.

## Tuning loop & maintenance

After ~5 PRs:
1. Scan Copilot's comments across the PRs.
2. Identify weak patterns: false positives, duplications with `/simplify` / `mutation-sentinel` / per-task subagent reviews, missed real issues.
3. Add ONE targeted rule per iteration (Goldilocks principle — minimum changes, observed failures only). Don't batch.
4. Verify on the next PR; refine if needed.

Treat instruction files as code:
- Version-controlled under git (already).
- PR changes to instruction files like any other code change.
- Run `superpowers:requesting-code-review` on significant rule additions.
- Re-run the ADR cross-check audit after every 5–10 new ADRs or any decision touching a convention rule.

## Out of scope (deliberately)

- ADR references in instruction-file bodies — Copilot won't follow them; Appendix A preserves the rule↔ADR map.
- Priority tier framing (CRITICAL / IMPORTANT / SUGGESTION) — over-engineered for cheap-gate role.
- Detailed Tailwind v3-vs-v4 migration rules — too granular, would produce noise.
- Specific function-length / cyclomatic complexity thresholds — `/simplify` covers.
- Frontend graph-library rules (e.g. cytoscape ban for ADR-0040 work) — defer until graph features land.
- Pact contract-test conventions — defer until Pact tests exist in the tree.
- Playwright E2E conventions — defer until `tests/Kartova.E2E/` exists.
- Tailwind theme-token rules (use `theme.css` variables, don't hardcode) — too detailed; produces noise.

## Risks

- **Both-agent contamination.** Coding agent will read review-tuned phrasing. Mitigation: bidirectional phrasing was a constraint during drafting; deny-list items are mostly content-bidirectional.
- **The 4 KB cap is not enforced anywhere.** Adding rules over time without measuring will silently truncate review-time content. Mitigation: every change to an instruction file should re-check the file's character count; safety budget is 3,800 per file.
- **Duplication with CLAUDE.md.** Conventions appear in both. Drift is the long-term risk. Mitigation: when an ADR changes a convention, update both files in the same PR.
- **Audit-driven rule debt.** Two of the three audits caught real issues (stale ADR-0090 amendment, missing ADR-grounded rules, MSW-not-installed). Future ADRs will silently invalidate rules unless we re-run an audit periodically.

---

## Appendix A: Rule ↔ ADR traceability map

This map preserves the rule→ADR relationship that's stripped from the live files. Use as a grep aid when an ADR changes; the corresponding instruction-file rule may need updating.

### Backend (F2)

| Rule | ADR(s) | Notes |
|---|---|---|
| Wolverine `IMessageBus` only | ADR-0028, ADR-0080 | MediatR / MassTransit explicitly rejected |
| Cross-module via `IMessageBus` or Kafka | ADR-0082 | `*.Contracts` is the only referenceable cross-module package |
| Outbound Kafka via Wolverine outbox | ADR-0080 | EF Core + Postgres outbox |
| Inbound Kafka via KafkaFlow | ADR-0081 | Per-key parallel-within-partition |
| Sync HTTP direct dispatch / no `InvokeAsync` | ADR-0093 | Wolverine scope narrowed |
| `ITenantScope` Begin/Commit lifecycle | ADR-0090 (addendum 2026-04-28) | `TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter` |
| `AddModuleDbContext<T>` | ADR-0090 | Tenant-owned DbContexts only |
| `AdminOrganizationDbContext` BYPASSRLS | ADR-0090 | Admin bypass exception |
| Migrations via `Kartova.Migrator` | ADR-0085 | No `Migrate()`/`EnsureCreated()` at app startup |
| `SET LOCAL app.current_tenant_id` | ADR-0012, ADR-0090 | RLS GUC name |
| `CursorPage<T>` + sortBy/sortOrder/cursor/limit | ADR-0095 | `[BoundedListResult]` exception with `// reason:` |
| `[ExcludeFromCodeCoverage]` narrow scope | (no ADR — `tests/Kartova.ArchitectureTests/ContractsCoverageRules.cs`) | Architecture test rule |
| Domain has no Infrastructure / Application refs | ADR-0028, ADR-0082 | Per-module Clean Architecture |
| Handlers may live in `*.Infrastructure` | ADR-0093 | Departure from textbook Clean Architecture |
| Webhooks: HMAC + retry + DLQ + idempotency + rate-limit + circuit breaker + 24h rotation overlap | ADR-0033 | "Use the shared pipeline; flag bespoke" — diff-visible portion only |
| OAuth tokens AES-256-GCM with per-tenant DEK | ADR-0077 | mTLS not used; flag plaintext / hand-rolled crypto |
| No raw secret-value columns | ADR-0078 | Pairs with ADR-0077 |
| Routes via `MapTenantScopedModule` / `MapAdminModule` | ADR-0092 | REST URL convention |
| `application/problem+json` errors | ADR-0091 | RFC 7807 |
| Structured logs with `tenant_id` + `correlation_id` | ADR-0058 | No `Console.WriteLine` |

### Frontend (F3)

| Rule | ADR(s) | Notes |
|---|---|---|
| `react-aria-components` (Untitled UI free-tier) | ADR-0094 | Components are *copied* via `npx untitledui@latest add` |
| `@untitledui/icons` | ADR-0094 | ADR-0088 (`lucide-react`) is superseded |
| Tailwind v4 utilities | ADR-0094 | Tokens at `web/src/styles/theme.css` |
| Dark mode `.dark-mode` class | ADR-0094 | NOT Tailwind default `.dark` |
| `next-themes` for theme switching | ADR-0094 | "Rebind to set `.dark-mode`" |
| TanStack Query for server state | ADR-0039 | React + TS + Vite stack |
| `openapi-fetch` typed client | (no ADR — project tooling) | Generated by `scripts/codegen.mjs` |
| `react-router-dom` v7 data routes | (no ADR — project convention; ADR-0039 generic) | |
| `react-oidc-context` `useAuth()` | ADR-0006, ADR-0007 | KeyCloak OIDC at protocol level |
| `useCursorList` + `useListUrlState` + `<DataTable>` | ADR-0095 | Cursor pagination is non-optional |
| Cursor query syntax `?sortBy=&sortOrder=&cursor=&limit=` (max 200) | ADR-0095 | Flag `?page=` / `?offset=` / `?perPage=` |
| RFC 7807 frontend error parsing | ADR-0091 | `type`, `title`, `detail`, `traceId`, `errors` |
| TypeScript strict, no `any` | ADR-0039 | TS strict mode |
| Forms: `react-hook-form` + `zod` | (no ADR — project convention from package.json) | |

### Tests (F4)

| Rule | ADR(s) | Notes |
|---|---|---|
| xUnit only | ADR-0083 | "xUnit + FluentAssertions" |
| NetArchTest in architecture-tests project | ADR-0083 | Mandatory CI gate |
| Testcontainers (Postgres + Kafka + ES + KeyCloak) | ADR-0083 | No in-memory provider for tenant data |
| `KartovaApiFixtureBase` / `KeycloakContainerFixture` | (no ADR — fixture base from `tests/Kartova.Testing.Auth/`) | |
| `CursorPage<T>` envelope assertions | ADR-0095 | List-endpoint integration tests |
| `application/problem+json` assertions | ADR-0091 | Error-path integration tests |
| Vitest + Testing Library + user-event | (no ADR — project tooling) | ADR-0039 names React+Vite stack only |
| Stryker mutation testing target ≥80% | (no ADR — explicitly out of scope per ADR-0083) | `stryker-config.json` is project tooling |

### Manifest (F1)

| Rule | ADR(s) | Notes |
|---|---|---|
| REST not GraphQL | ADR-0029, ADR-0034 | OpenAPI 3.x auto-generated |
| Wolverine not MediatR/MassTransit | ADR-0028, ADR-0080 | |
| RLS not schema-per-tenant | ADR-0001, ADR-0012 | |
| HTTPS polling not gRPC for agent | ADR-0042 | Proxy-friendly |
| `Kartova.slnx` not `.sln` | ADR-0089 (and per-project `.slnx` adoption) | |
| Modular monolith via `*.Contracts` packages | ADR-0082 | |

---

## Appendix B: Cross-check audit findings (drafting trail)

Two layers of audit shaped this design. Notable catches that altered the draft:

**ADR cross-check (3 runs across backend / frontend / tests):**
- ADR-0090 was amended 2026-04-28 (`TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter`). Initial draft was stale.
- ADR-0028 + ADR-0082 govern Clean Architecture per module. Initial draft cited ADR-0027 incorrectly.
- ADR-0093 narrowed Wolverine scope; handlers may live in `*.Infrastructure`. Initial draft would have falsely flagged this.
- ADR-0094 dark-mode class is `.dark-mode`, NOT Tailwind default `.dark`. Initial frontend draft missed this.
- MSW is not installed in the project — initial frontend test rule referenced it incorrectly.
- ADR-0091 (RFC 7807 problem+json) was missed in initial draft for both backend and frontend.
- ADR-0092 (`MapTenantScopedModule` / `MapAdminModule` routing) was missed in initial backend draft.

**Expert optimization (3 runs):**
- Vacuous-assertion rule had a Gopu determinism violation ("behavior-asserting check" requires semantic judgement). Replaced with enumerated assertion patterns.
- Webhook rule had a Gopu violation (asking Copilot to detect 7 concerns including DI/composition-root config not visible in diff). Replaced with "use shared pipeline; flag bespoke."
- `*Async()` await rule was redundant with CS4014 / async analyzers. Moved to deny-list.
- Several wordy phrasings tightened to imperative, freeing budget for new rules.
- ❌/✅ tables were filled out with concrete identifier pairs (originally placeholders).

---

## Appendix C: Research provenance

Rules drafted with reference to research at:
- `C:\Projects\Private\SecondBrain\kb\gold\ai-ml\copilot-instruction-rule-patterns.md`
- `C:\Projects\Private\SecondBrain\kb\gold\ai-ml\github-copilot-code-review.md`

Patterns explicitly applied:
- **Goldilocks zone**: minimum rules; add only after observed failures.
- **Deny-list at top**: Jones tuning pattern; primes the model.
- **Paired ❌/✅ examples**: GitHub Blog "Mastering instructions files" + awesome-copilot generic template + UI5/webcomponents (in the wild).
- **Identifiers Copilot can match in diff**: Class names, attribute names, helper names — load-bearing. File paths / ADR refs / prose abstractions stripped.
- **Bidirectional phrasing**: file is read by both review and coding agent; rules phrased so both agents benefit.
- **Gopu determinism caveat**: don't ask Copilot to deterministically detect what isn't visible in a diff (rotation windows, breaker config, semantic judgements).
