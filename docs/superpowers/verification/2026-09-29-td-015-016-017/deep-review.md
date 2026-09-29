# Gate 8 — deep-review (TD-015 / TD-016 / TD-017)

**Range:** f0a62b1..fed571c (branch `chore/tech-debt-td-015-016-017`, OPEN → pre-merge gate) · **Date:** 2026-09-29
**Read against:** spec `docs/superpowers/specs/2026-09-29-td-015-016-017-design.md`; plan `docs/superpowers/plans/2026-09-29-td-015-016-017-plan.md` (Global Constraints, Impact Analysis, Review Focus, Tasks 0–7); ADR-0060, ADR-0082, ADR-0084, ADR-0113, ADR-0116, ADR-0118 (read in full where relevant); `docs/TESTING-STRATEGY.md` / ADR-0097 tiers; CLAUDE.md DoD. Prior gates read and not re-reported: `requesting-code-review.md` (gate 6), `review-pr.md` (gate 7), `simplify.md` (gate 5), `dod.md`.
**Mutation report:** none exists for this slice. Stryker is not a DoD gate; no mutation evidence was considered.

### Overview

The slice moves the health, OpenAPI and version routes out of `Program.cs` into `SystemEndpoints : IModuleEndpoints`, so the `EndpointRouteRules` arch sweep now covers them (TD-015). It adds an ops-admin allowlist rule and a source guard on `Program.cs`. Both SPAs now read OIDC and API config at runtime from an nginx-rendered `/config.js` fed by `KARTOVA_*` env vars, wired through the Dockerfile, compose, Helm and a CI container check (TD-016). A 30-second `sessionStorage` re-auth marker trips a full-app "session rejected" panel instead of looping `signinRedirect` on a persistent 401 (TD-017).

### Blocking-class issues

None.

Every spec section is implemented and no plan acceptance criterion is unmet. Gates 3, 8, 9 and 10 plus the terminal re-verify are still pending in the ledger, which is expected at this point and not a finding in itself. There are two divergences from the spec. The first, `retry` re-marking instead of clearing, is plan-sanctioned and gate 6 ruled it better. The second, the PROD `console.warn` from gate-7 R1, was never written back into the spec (Nit N2).

### Should-fix issues

- **ADR-0113 (and its README index row) still describe the gap TD-016 just closed.**
  - **Evidence:**
    - `docs/architecture/decisions/ADR-0113-e2e-suite-compose-nightly.md:70-75`: "No build args and no runtime config injection: the container is built with its existing defaults (`VITE_API_BASE_URL=…`, `VITE_OIDC_AUTHORITY=…`)".
    - `ADR-0113…md:241-247`: "Real-k8s URL injection remains an open gap … no runtime `config.js`-style injection … Tracked as a follow-up".
    - `docs/architecture/decisions/README.md:244`: "Known limits: … real-k8s URL injection deferred."
    - Both statements are now false. `docker-compose.yml:141-155` and `:172-175` inject `KARTOVA_*` into the same `web`/`web-admin` services the E2E suite drives, and `web/Dockerfile` has no `VITE_*` ARGs left.
    - ADR-0118 and ADR-0116 don't contradict the change: neither mentions build-time `VITE_*`, and ADR-0116's `script-src 'self'` is satisfied by same-origin `/config.js`. ADR-0060 names no mapping location; its code sample is illustrative `app.MapHealthChecks(...)`, which is still accurate.
  - **Disagreement with gate 6:** gate 6 declined this as "historical ADR record; spec does not ask". I disagree. The README index is the keyword source CLAUDE.md tells the assistant to consult before architectural suggestions ("Before architectural suggestions: check ADR keyword index"). A live "deferred" limit that is no longer true will steer a future slice to re-solve TD-016.
  - **Impact:** anyone reading the ADR library believes the web image still needs per-environment builds and that E2E runs without runtime injection.
  - **Fix:**
    - Append a dated amendment note to ADR-0113, for example "Amended 2026-09-29 (TD-016): runtime `/config.js` from `KARTOVA_*` env; compose sets them; the real-k8s URL-injection gap is closed". Strike or annotate the two paragraphs.
    - Change the README row's "Known limits" to drop "real-k8s URL injection deferred".
    - Per CLAUDE.md, preview the amendment wording with the owner before saving. It is a docs-only change.

- **The DoD evidence ledger is behind: `gate-findings.yaml` is empty, and the E2E and planted-violation evidence is not cited.**
  - **Evidence:**
    - `docs/superpowers/verification/2026-09-29-td-015-016-017/gate-findings.yaml:38` reads `findings: []` and `:15` reads `terminal_commit: <short-sha>`. Gates 2, 5, 6 and 7 raised about 25 findings with real/delusion outcomes: `progress.md`, `simplify.md` S1–S11, `requesting-code-review.md` I-1 plus Minors 1–7, and `review-pr.md` R1–R8. CLAUDE.md requires them appended "the moment a gate raises one". Every prior slice's file is populated (7 to 39 entries).
    - `dod.md:45-48` (gate 3) and `:94-97` (gate 9) are still templates. `e2e-run.txt` (11/11) is referenced nowhere in `dod.md`. The spec's Testing section requires the planted-violation proofs to be "recorded in the ledger", but they live only in scratch `.superpowers/sdd/…/task-1-report.md` and `gate6-fix-report.md`. Gate 6 Minor 3 already asked for this, and the controller ruled it "adds to ledger (gate 3 row)". It has not happened yet.
    - `e2e-run.txt` was produced at 757980c. Two later commits changed code that runs on every page load or every 401: a2a2405 refactored `useApiAuthBridge` into `reauthenticate()`, and e9630cf added the PROD `console.warn` in `runtimeConfig.ts`. The CLAUDE.md E2E-impact trigger requires the affected specs to be run before merge. The recorded run predates the final code.
  - **Impact:**
    - Gate-effectiveness telemetry for this slice is lost.
    - The `.claude/hooks/dod-check.js` stop hook will block the completion claim, because `terminal_commit` is missing.
    - The E2E-impact trigger is evidenced against superseded code.
  - **Fix:**
    - Populate `gate-findings.yaml` from the four reports: gate slug, severity, `real`/`delusion` verdict, and fix sha.
    - At the terminal re-verify, re-run `bash e2e/run.sh` on the final commit and overwrite `e2e-run.txt`.
    - In the gate 3 row, cite `e2e-run.txt` (with commit), the 3 + 2 planted-violation outcomes (test name + FAIL message), and the frontend evidence (`npx tsc -b` exit 0 and the `npx vitest run` counts, per the CLAUDE.md "frontend type gate" agreement).
    - Set `terminal_commit` in both files.

### Nits

1. **The breaker trips on a *tokenless* 401 but is cleared only by an *authenticated* response. The asymmetry can show the panel for a non-session failure.**
   - **Evidence:** `web/src/shared/api/createAuthedApiClient.ts:31` calls `onUnauthorized()` for every 401 regardless of `Authorization`, and `web/src/shared/oidc/useApiAuthBridge.ts:72` trips on any 401 inside the window. Line 32 clears only when `request.headers.has("Authorization")`.
   - **Impact:** a request fired with a null token within 30 s of a marked redirect shows "the API refused the new sign-in" although no token was ever sent. An example is a render before `tokenRef` is populated, which is the PR #47 race class. This is recoverable via Try again and unlikely today.
   - **Fix:** in `createAuthedApiClient`, pass the token-presence to the handler (`onUnauthorized({ hadToken })`), or skip the breaker branch in `useApiAuthBridge` when no token was sent: redirect as before, without tripping.
2. **The spec is not updated for two post-plan behaviours.**
   - **Evidence:**
     - `docs/superpowers/specs/2026-09-29-td-015-016-017-design.md:109` says "`retry` clears the marker + flag + `redirectingRef`", and `:136` says "retry clears and redirects". The implementation re-marks: `useApiAuthBridge.ts:82-85` via `reauthenticate()`.
     - The Error-handling table at `:121` omits the PROD one-time `console.warn` added in gate 7 (`web/src/shared/config/runtimeConfig.ts:684-690`).
   - **Impact:** the tracked spec, which is the design of record, disagrees with the code on two observable behaviours.
   - **Fix:** amend lines 109 and 136 to "re-marks and redirects (one more round-trip)". Add a table row: "fallback to built-in default in a production build → one `console.warn` per key naming the `KARTOVA_*` var".
3. **`Program_maps_no_routes_directly` still misses the middleware form of a health endpoint.**
   - **Evidence:** `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs:218` matches only `\.Map(?!Endpoints\()\w*\(`.
   - **Impact:** `app.UseHealthChecks("/health/x")` in `Program.cs` creates a terminal health endpoint that bypasses both endpoint authorization and this sweep. That is the exact class TD-015 closes, and the health-check API offers it as a one-line alternative. This differs from gate-5 S11, which accepted the heuristic generally; this is a named, concrete gap.
   - **Fix:** extend the pattern to `\.(Map(?!Endpoints\()\w*|UseHealthChecks|UseEndpoints)\(`. Plant `app.UseHealthChecks("/x");` once to prove it discriminates.
4. **`tech-debt.md` marks the items `done` before merge.**
   - **Evidence:** `docs/engineering/tech-debt.md:281`, `:302` and `:323` read "done (branch `chore/tech-debt-td-015-016-017`)", while gates 3/8/9/10 are pending.
   - **Impact:** CLAUDE.md's honest status until all ten gates pass is "implementation staged"; the register says done.
   - **Fix:** at merge, replace "branch …" with the PR number and ledger path, as TD-012 at `:226` does (`DoD ledger: docs/superpowers/verification/…/dod.md`).
5. **ADR-0060's security table still describes `/health/detailed` as "Bearer JWT, Operations role".**
   - **Evidence:** `docs/architecture/decisions/ADR-0060-three-probe-health-checks-aspnet-core-framework.md:76`. ADR-0118 (line 23) and now `SystemEndpoints.cs:38-42` bind it to the `PlatformAdmin` scheme plus role. The drift predates this slice, but the slice re-pins that binding and cites ADR-0060 as "unchanged".
   - **Fix:** add a one-line "Amended by ADR-0118: `PlatformAdminOnly` (operator-realm scheme + role)" under that table. Optional; it can be bundled with the ADR-0113 note.

### Missing tests

- **The runtime config actually reaches the four consumers (TD-016 core claim; spec "Consumers" + D2).**
  - **Gap:**
    - `runtimeConfig.test.ts` covers only the resolver.
    - `check-runtime-config.sh` proves nginx renders `/config.js` but never loads the bundle.
    - Compose sets `KARTOVA_*` to exactly the built-in defaults (`docker-compose.yml:142-144`, `:153-155`), so E2E (11/11) cannot tell "runtime value used" from "fallback used".
    - A consumer passing the wrong key, for example `"oidcAuthority"` for the client id, or reverting to a direct `import.meta.env` read, would pass every automated tier. Only the pending manual gate 9 would see it.
  - **Test that should exist:** web vitest, new `web/src/shared/config/__tests__/runtimeConfigConsumers.test.ts`.
    - For each consumer: `vi.resetModules()`, set `window.__KARTOVA_CONFIG__ = { apiBaseUrl: "https://api.runtime", oidcAuthority: "https://kc.runtime/realms/x", oidcClientId: "rt-client" }`, then dynamically import the module.
    - Assert `(await import("@/features/catalog/api/client")).API_BASE_URL === "https://api.runtime"`, and the same for `@/admin/api/client`'s `ADMIN_API_BASE_URL`.
    - For the OIDC config: mock `@/shared/oidc/authConfig`'s `buildOidcConfig` and assert it was called with `authority: "https://kc.runtime/realms/x", clientId: "rt-client"` after importing `@/shared/auth/AuthProvider` and `@/admin/providers`.

- **A tokenless 401 does not trip the breaker (pairs with Nit 1, if adopted).**
  - **Test that should exist:** web vitest, `useApiAuthBridge.test.tsx`. With `markReauthAttempt()` set and the handler invoked for a 401 on a request without `Authorization`, assert `signinRedirect` is called once and `bridge.reauthFailed === false`. Add the matching `createAuthedApiClient.test.ts` case: a 401 with `getToken = () => null` passes `hadToken: false`.

### What looks good

- **TD-015 extends the existing convention instead of creating a new host path.**
  - `src/Kartova.Api/SystemEndpoints.cs` is picked up by the convention-based `IModuleEndpoints` discovery the sweep already uses (ADR-0082 NetArchTest; `AssemblyRegistry.AllProduction()` already includes `Kartova.Api`).
  - URLs, predicates, writers and the `PlatformAdminAuth.Policy` binding are byte-identical (ADR-0060 contract kept).
  - The only contract drift is the OpenAPI `operationId`/tag on `/api/v1/version`, and it is regenerated in `web/openapi-snapshot.json`.
  - The class carries `[ExcludeFromCodeCoverage]` per the composition-class convention.
- **`OpsAdminRoutes` is an explicit allowlist with anti-vacuity.** `Every_ops_admin_route_requires_PlatformAdminOnly` (`EndpointRouteRules.cs:181-196`) demands exactly one match, the policy, and no `AllowAnonymous` — the `AuthorizationMiddleware` skip that the admin-prefix rule already guards. A carve-out is safer than widening the prefix rule.
- **The nginx design is correct in the details.**
  - Exact-match `location = /config.js` outranks the `*.js` immutable rule.
  - `no-store` means a pod-restart config change lands on the next load.
  - The Dockerfile `ENV KARTOVA_*=""` avoids envsubst's literal-`${…}` leak.
  - `web/scripts/check-runtime-config.sh` asserts both the set and unset paths, content-type, cache header, the HTML `<script>` tag and the surviving CSP. It is wired into CI and `ci-local.sh`, which turns the gate-4 container seam into a real regression test.
- **The TD-017 breaker is defensively scoped.**
  - `reauthMarker.ts` stores only a timestamp.
  - Every storage access fails open to today's behaviour.
  - A backwards clock reads as "not recent".
  - A redirect that never left the page clears the marker (`useApiAuthBridge.ts` `auth.error` effect), so an unreachable KeyCloak can't be mistaken for a rejected session.
  - Both bridges share one hook, so tenant and admin can't diverge.
- **Deploy wiring is guarded against cross-wiring.** `deploy/helm/render-check.sh:27-31` asserts a web-only and an admin-only override each land on the right Deployment, and that the var renders on both. The unsafe-character constraint (`'` `"` `$` `\`) is documented at every place an operator sets the value (`values.yaml`, both templates, `deploy/README.md`).
