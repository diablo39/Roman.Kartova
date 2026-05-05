# Copilot Code Review Instructions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create four Copilot instruction files (manifest + backend + frontend + tests) in the Kartova repo and enable auto-review on `master`.

**Architecture:** Manifest at `.github/copilot-instructions.md` (repo-wide rules + global deny-list); three path-scoped files under `.github/instructions/` matching backend `*.cs`, frontend `*.{ts,tsx}`, and tests across both. Each file ≤ 3,800 characters (safety budget under the 4,000-char review cap; content past the cap is silently dropped during review). Both Copilot agents (review + coding agent) read all files (no `excludeAgent`). The existing branch protection on `master` continues to require human approval — Copilot's "Comment" review can't satisfy required-reviewer rules and is intentionally additive, not a replacement.

**Tech Stack:** Markdown instruction files with YAML frontmatter; GitHub branch ruleset for auto-review configuration; `gh` CLI for PR operations; existing branch protection retained for human-approval gate.

**Spec reference:** `docs/superpowers/specs/2026-05-05-copilot-review-instructions-design.md` — full file contents, decision rationale, rule↔ADR traceability appendix.

---

## Task 1: Create branch and the four instruction files

**Files:**
- Create: `.github/copilot-instructions.md`
- Create: `.github/instructions/backend.instructions.md`
- Create: `.github/instructions/frontend.instructions.md`
- Create: `.github/instructions/tests.instructions.md`

- [ ] **Step 1: Verify on master with clean working tree**

```bash
git status
git branch --show-current
```

Expected: `working tree clean` (or only the spec/plan docs uncommitted from the brainstorming session); current branch is `master`.

If there are unrelated uncommitted changes, stash them or commit them on a separate branch before continuing.

- [ ] **Step 2: Create the feature branch**

```bash
git checkout -b feat/copilot-review-instructions
```

Expected: `Switched to a new branch 'feat/copilot-review-instructions'`.

- [ ] **Step 3: Verify `.github/instructions/` does not yet exist**

```bash
ls .github/
ls .github/instructions/ 2>&1 || echo "(directory does not yet exist — expected)"
```

Expected: `.github/copilot/` directory exists (the existing CLI skill folder); `.github/instructions/` does NOT exist. If `.github/instructions/` already exists with files, stop and reconcile with the user before continuing.

- [ ] **Step 4: Create `.github/copilot-instructions.md`**

Write this exact content (no surrounding fences in the actual file — content starts with `# Copilot review...`):

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

- [ ] **Step 5: Create `.github/instructions/backend.instructions.md`**

Write this exact content (the YAML frontmatter is part of the file):

````markdown
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
````

- [ ] **Step 6: Create `.github/instructions/frontend.instructions.md`**

Write this exact content:

````markdown
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
````

- [ ] **Step 7: Create `.github/instructions/tests.instructions.md`**

Write this exact content:

````markdown
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
````

- [ ] **Step 8: Verify character counts under the 3,800 safety budget**

Use `wc -m` (character count) NOT `wc -c` (byte count) — the ❌/✅ emojis are multi-byte UTF-8.

```bash
wc -m .github/copilot-instructions.md .github/instructions/backend.instructions.md .github/instructions/frontend.instructions.md .github/instructions/tests.instructions.md
```

Expected output: each file ≤ 3,800 characters.
- Manifest (`copilot-instructions.md`): around 1,100 chars
- `backend.instructions.md`: around 3,560 chars
- `frontend.instructions.md`: around 3,720 chars
- `tests.instructions.md`: around 3,810 chars (right at safety budget — see Step 8a if over)

- [ ] **Step 8a: If `tests.instructions.md` is over 3,800 chars**

If `wc -m` reports tests file > 3,800 chars, trim by removing the `data-testid`/`querySelector` row from the table at the bottom (the rule above already covers it). Specifically delete this line from the `## Quick reference` table:

```
| `container.querySelector(".btn")`                  | `screen.getByRole("button", { name: /save/i })` |
```

(That line is not currently in the file because we already trimmed in the spec — verify it's actually missing. If the file is still over after this potential trim, ask the user before further trimming.)

- [ ] **Step 8b: Verify no other files were unintentionally modified**

```bash
git status
```

Expected: only the four new files are listed; no modifications to other tracked files.

- [ ] **Step 9: Verify CI workflow not affected**

```bash
cat .github/workflows/ci.yml | head -20
```

Expected: existing `ci.yml` unchanged. Confirm the file's first lines look as they did before this work started. The new instruction files do not interact with CI.

- [ ] **Step 10: Stage and commit**

```bash
git add .github/copilot-instructions.md .github/instructions/backend.instructions.md .github/instructions/frontend.instructions.md .github/instructions/tests.instructions.md
git status
git commit -m "$(cat <<'EOF'
feat: add Copilot review and coding-agent instruction files

Adds .github/copilot-instructions.md (manifest + repo-wide rules + global
deny-list) and three path-scoped files under .github/instructions/ for
backend (.cs), frontend (.ts/.tsx), and tests. Both Copilot review and
coding agent read all four files (no excludeAgent). Each file under the
3,800-char safety budget (4,000-char review-time cap).

Spec: docs/superpowers/specs/2026-05-05-copilot-review-instructions-design.md
EOF
)"
```

Expected: one commit added; `git log --oneline -1` shows the new commit.

- [ ] **Step 11: Push the branch**

```bash
git push -u origin feat/copilot-review-instructions
```

Expected: branch pushed; tracking set up against `origin`.

- [ ] **Step 12: Open a draft PR**

```bash
gh pr create --draft --title "feat: add Copilot review and coding-agent instruction files" --body "$(cat <<'EOF'
## Summary

Adds the four-file Copilot instruction setup:
- `.github/copilot-instructions.md` — manifest + repo-wide rules + global deny-list
- `.github/instructions/backend.instructions.md` — `applyTo: src/**/*.cs`
- `.github/instructions/frontend.instructions.md` — `applyTo: web/src/**/*.{ts,tsx}`
- `.github/instructions/tests.instructions.md` — backend test files + frontend test files

Files serve **both** Copilot code review and Copilot coding agent (no `excludeAgent`).

## Notes

- **Copilot review on this PR is un-tuned.** Copilot reads instructions from the PR base branch (master); master doesn't yet have these files. Treat Copilot's review on this PR as not-yet-tuned — its comments may not reflect the final ruleset.
- **Auto-review ruleset configuration is a follow-up step** (Tasks 3–4 of the implementation plan). After this PR merges, configure the branch ruleset on master to enable auto-review.
- See `docs/superpowers/specs/2026-05-05-copilot-review-instructions-design.md` for full design rationale, file contents, and rule↔ADR traceability map.

## Test plan

- [ ] Verify char counts on each file < 3,800 (run `wc -m`)
- [ ] Verify `.github/workflows/ci.yml` unchanged
- [ ] Verify git status shows only the four intended files
EOF
)"
```

Expected: a draft PR is created on `origin`; PR URL is printed to stdout. Save the PR number for later.

---

## Task 2: User reviews and merges PR

This task is mostly user action; the agent verifies state at the end.

- [ ] **Step 1: User reviews the PR diff in GitHub UI**

Verify visually that the four files match the spec contents. Specifically check:
- `.github/copilot-instructions.md` exists and has the manifest content
- `.github/instructions/backend.instructions.md` has `applyTo: "src/**/*.cs"` frontmatter
- `.github/instructions/frontend.instructions.md` has `applyTo: "web/src/**/*.{ts,tsx}"` frontmatter
- `.github/instructions/tests.instructions.md` has the test-paths `applyTo` glob
- No other files modified

- [ ] **Step 2: User marks the PR as ready for review**

In GitHub UI: click "Ready for review" button on the draft PR.

- [ ] **Step 3: Required CI checks pass**

Verify GitHub-required CI checks are green. The new files don't touch C# or TypeScript code, so build and test should pass without changes.

- [ ] **Step 4: User self-approves and merges (or another approver merges)**

Either:
- User leaves an approving review (if they have permissions), then merges via "Squash and merge" (recommended for solo work) or "Rebase and merge" per project preference.
- Or another team member approves and merges.

**Note on Copilot review on this PR:** Copilot may post a "Comment" review on this PR with no rule context (since master doesn't have the instructions yet). Its feedback may be generic style/typo-level and is safe to ignore for this specific PR.

- [ ] **Step 5: Verify merge landed**

```bash
git checkout master
git pull
ls .github/copilot-instructions.md .github/instructions/
```

Expected: all four files present on master:
- `.github/copilot-instructions.md`
- `.github/instructions/backend.instructions.md`
- `.github/instructions/frontend.instructions.md`
- `.github/instructions/tests.instructions.md`

- [ ] **Step 6: Delete the local feature branch**

```bash
git branch -d feat/copilot-review-instructions
```

Expected: local branch deleted (remote branch is automatically deleted by GitHub if "Automatically delete head branches" is enabled in repo settings; otherwise delete via `git push origin --delete feat/copilot-review-instructions`).

---

## Task 3: Configure auto-review branch ruleset (USER ACTION)

This task is GitHub UI only — no shell commands. The agent provides exact toggle states; the user clicks them.

- [ ] **Step 1: Navigate to the ruleset creation page**

In GitHub UI: Repo Settings → Rules → Rulesets → New branch ruleset.

- [ ] **Step 2: Set the ruleset name and enforcement**

| Field                | Value                            |
|----------------------|----------------------------------|
| Ruleset Name         | `Copilot auto-review (master)`   |
| Enforcement status   | `Active`                         |

- [ ] **Step 3: Set target branches**

Under "Target branches" → "Add target" → "Include default branch" (which is `master`).

If the default branch is named differently in this repo, manually add `master` as an inclusion pattern.

- [ ] **Step 4: Toggle ON: "Automatically request Copilot code review"**

Scroll down to the Branch rules section. Find the "Automatically request Copilot code review" rule and toggle it ON.

- [ ] **Step 5: Toggle ON: "Review new pushes"**

Within the Copilot code review rule's settings (after toggling it on), expand its options. Toggle "Review new pushes" ON. This causes Copilot to re-review on every push to a PR (earliest-feedback role).

- [ ] **Step 6: Toggle ON: "Review draft pull requests"**

Same Copilot rule's settings. Toggle "Review draft pull requests" ON. This causes Copilot to review draft PRs (don't wait for ready-for-review).

- [ ] **Step 7: Save the ruleset**

Click "Create" at the bottom of the page.

Expected: ruleset is listed in Settings → Rules → Rulesets with status `Active`, target `master`.

---

## Task 4: Verify branch protection still requires human approver (USER ACTION)

- [ ] **Step 1: Navigate to branch protection / rulesets**

In GitHub UI: Repo Settings → Rules → Rulesets, OR Settings → Branches → Branch protection rules (if using legacy protection).

- [ ] **Step 2: Verify human-approval requirement on master**

Confirm that the EXISTING `master` branch protection (separate from the new Copilot ruleset) still has:
- "Require a pull request before merging" — ON
- "Required approving reviews" — ≥ 1
- "Require approval of the most recent reviewable push" — ON (recommended)

If either is missing or set to 0, restore — Copilot's "Comment" review cannot satisfy required-reviewer rules. Without human approval as a gate, anyone could merge to master.

- [ ] **Step 3: Confirm no conflict between rulesets**

Confirm that the new `Copilot auto-review (master)` ruleset does NOT include any rule that bypasses or overrides the human-approval requirement (it shouldn't — the toggles in Task 3 are review-request automation, not merge gating).

---

## Task 5: Calibration PR

The goal: open a tiny PR after the rules are loaded on master and observe Copilot's review behavior.

- [ ] **Step 1: Pick or create a tiny calibration change**

Pick the smallest reasonable real change from `docs/product/CHECKLIST.md` (preferred — uses real backlog), OR create a deliberately-tiny non-functional change such as updating a comment in one `.cs` file and one `.tsx` file. The change must touch at least one file under each of the three `applyTo` globs (backend, frontend, tests) if you want all three path-scoped files to load — but that's not required for a first calibration; touching one file is enough to confirm Copilot fires.

If picking a real backlog item, pause this plan and run that task through its own slice plan first; then return here once it's open as a PR.

If creating an artificial calibration change, use a minimal change like adding a `// calibration: copilot-review-test` comment to one C# file and one TSX file — non-semantic, easy to revert.

- [ ] **Step 2: Create a calibration branch and commit**

```bash
git checkout master
git pull
git checkout -b chore/copilot-review-calibration
# (make the calibration change as decided in Step 1)
git add <changed files>
git commit -m "chore: copilot review calibration (will be closed unmerged)"
git push -u origin chore/copilot-review-calibration
```

- [ ] **Step 3: Open a draft PR**

```bash
gh pr create --draft --title "chore: copilot review calibration" --body "$(cat <<'EOF'
## Purpose

Calibration PR — verifies that Copilot auto-review fires on PRs against master after the instruction-file rollout. Will be closed without merging.

## What to check

- Copilot posts a "Comment" review on this PR within ~1 minute of opening
- Copilot's comments do NOT contain items from the deny-list (no formatting nits, no Tailwind v3 advice, no MediatR suggestions, no ad-hoc `{ "error" }` shape suggestions)
- If Copilot has nothing to say, that is also a valid outcome — the test is whether it FIRES, not whether it has substantive feedback on a calibration change
EOF
)"
```

Expected: a draft PR opens. Save the PR number.

- [ ] **Step 4: Wait for Copilot review**

Wait roughly 60–90 seconds. Copilot review typically completes in under 30 seconds, but the auto-request from the ruleset can add latency.

```bash
gh pr view <pr-number> --json reviews,comments | jq '.reviews[] | {state, author: .author.login, body}'
```

Expected: at least one review by `copilot-pull-request-reviewer[bot]` (or the current bot identity) with `state: COMMENTED`.

If no review appears after 5 minutes, troubleshoot:
- Verify the ruleset is `Active` (Task 3 Step 7).
- Verify the PR's base branch is `master` (the ruleset's target).
- Check repo Settings → Code & automation → Copilot → Code review → "Use custom instructions when reviewing pull requests" is ON (default).

- [ ] **Step 5: Inspect Copilot's comments**

Read each comment by Copilot. Verify:

(a) **No deny-list violations.** Copilot should NOT comment on:
- formatting / whitespace
- `var` vs explicit type
- arrow-vs-function-declaration
- generic naming-convention nits
- mutation-score gaps
- MediatR/MassTransit migration suggestions
- GraphQL migration suggestions
- Tailwind v3 advice
- formatting-only changes

(b) **Comments target the conventions encoded in the instruction files** (if it has comments at all). E.g., if the calibration touched a file that violates a rule (intentionally or otherwise), Copilot should flag it according to the rule's phrasing.

(c) **No comments referencing fictional identifiers** — Copilot shouldn't suggest replacing things with classes/methods that don't exist in the codebase.

- [ ] **Step 6: Close the calibration PR without merging**

If the calibration was an artificial change, close and revert:

```bash
gh pr close <pr-number> --comment "Calibration complete; closing without merge."
git checkout master
git branch -D chore/copilot-review-calibration
git push origin --delete chore/copilot-review-calibration
```

If the calibration was a real backlog item, follow the normal slice-completion path (DoD verification, etc.) — it's no longer a calibration PR, just a regular PR.

- [ ] **Step 7: Update the spec doc with calibration outcome**

Append to `docs/superpowers/specs/2026-05-05-copilot-review-instructions-design.md` (or open a follow-up commit on master) a brief "Calibration outcome" subsection in Appendix B noting:

- Date of calibration
- PR number
- Whether Copilot fired (yes/no)
- Time-to-first-comment
- Any deny-list violations observed (none expected; flag if found)
- Any rule-targeting hits or misses worth tuning

```bash
# example
git checkout master
git pull
# edit the spec doc, append the calibration note
git add docs/superpowers/specs/2026-05-05-copilot-review-instructions-design.md
git commit -m "docs: append Copilot review calibration outcome to spec"
git push
```

---

## Task 6: Document tuning loop reminder

This task is a setup-only step for the maintenance loop described in the spec.

- [ ] **Step 1: Create a calendar / todo reminder**

Set a reminder to scan Copilot's review comments after ~5 PRs land on master (or whenever feels right based on PR cadence).

- [ ] **Step 2: When the reminder fires, run the tuning loop**

(This is not part of this plan's execution — it's the ongoing maintenance loop, captured here for completeness.)

1. Pull recent PRs and their Copilot review threads.
2. Identify weak patterns: false positives, duplications with `/simplify` / `mutation-sentinel` / per-task subagent reviews, missed real issues.
3. Add ONE targeted rule per iteration (Goldilocks principle — minimum changes, observed failures only). Open a follow-up PR modifying the relevant `.instructions.md` file.
4. Re-verify char count under 3,800 after the addition.
5. Verify on the next PR; refine if needed.

---

## Spec coverage check

| Spec section                         | Plan task(s) covering it                                |
|--------------------------------------|---------------------------------------------------------|
| F1 — manifest content                | Task 1 Step 4                                           |
| F2 — backend file content            | Task 1 Step 5                                           |
| F3 — frontend file content           | Task 1 Step 6                                           |
| F4 — tests file content              | Task 1 Step 7                                           |
| Implementation steps 1–2 (file create + verify char count) | Task 1 Steps 4–8                          |
| Implementation step 3 (ruleset config) | Task 3                                                |
| Implementation step 4 (branch protection retained) | Task 4                                      |
| Implementation step 5 (calibration PR) | Task 5                                                |
| Tuning loop maintenance              | Task 6                                                  |
| Out-of-scope items (Section 3C)      | Not implemented — by design                             |
| Risks (Section 3D)                   | Not implemented — captured in spec; mitigations live in process |

All required spec sections are covered. The bidirectional-phrasing constraint and rule↔ADR traceability are encoded directly in the file contents (Task 1 Steps 4–7), not as separate plan tasks.

---

## Notes for execution

- Tasks 1, 5, and 6 are agent-executable.
- Tasks 2, 3, and 4 require user action in the GitHub UI; the agent provides exact toggle states and verification commands.
- The plan does NOT include the full Definition-of-Done verification (build, fitness tests, mutation testing, etc.) because the deliverables are config-only Markdown files — no production code is changed, no tests need updating, no runtime behavior changes. The DoD reduces to: char-count verification + CI green + human-approval merge.
- If at any point Copilot review fires on a PR with the rules loaded and the comments quality is consistently below useful (e.g., > 50% noise rate), pause the rollout and run an immediate tuning iteration before continuing — don't accept noise as the new normal.
