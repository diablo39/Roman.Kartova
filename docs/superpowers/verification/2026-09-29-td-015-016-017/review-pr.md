# Gate 7 — `/pr-review-toolkit:review-pr` — branch f0a62b1..b4f27f8

Agents: standing set **code-reviewer + pr-test-analyzer + type-design-analyzer**, plus **silent-failure-hunter** because the diff adds try/catch (`reauthMarker.ts`) and auth-error branches. comment-analyzer was skipped because the diff is code-heavy. Each agent read `requesting-code-review.md` so it wouldn't re-report gate-6 items.

## Findings and dispositions

| # | Agent | Sev | Finding | Disposition |
|---|---|---|---|---|
| R1 | silent-failure-hunter | High→**Important** | `resolveConfigValue`: fallback to the localhost default is silent. A Helm deploy that forgets `web.config.*` / `webAdmin.config.*`, or a `/config.js` that fails to load, gives opaque OIDC/API failures with no console signal. | **fix** — `console.warn` once per key when a production build falls back to the built-in default. Not a hard fail (spec D2 stands). |
| R2 | pr-test-analyzer | 5 | Nothing automated asserts the CSP header survives the nginx template edits; the plan only has a manual curl check. | **fix** — `check-runtime-config.sh` asserts the HTML response still carries `Content-Security-Policy` with `script-src 'self'`. |
| R3 | silent-failure-hunter | Low | `check-runtime-config.sh start()`: a container that crash-loops shows up as a generic 30 s timeout. | **fix** — fail fast when the container is no longer running (distinct message plus `docker logs`). |
| R4 | (gate-6 deferred T3) | Minor | HTML check pipes `curl \| grep -q` under `pipefail`, a latent SIGPIPE false fail. | **fix** now, since the script is being touched anyway — use the temp-file pattern. |
| R5 | type-design-analyzer | Suggestion | `useApiAuthBridge` returns `{ reauthFailed, retry }`, so "children rendered while failed" is representable → discriminated union. | skip — a union doesn't force callers to branch either; both call sites early-return and each is covered by a panel integration test. |
| R6 | silent-failure-hunter | Medium→Minor | `reauthMarker` catch blocks swallow every error without logging. | skip — spec: a storage failure means "no marker", which is today's behaviour; logging would be noise in privacy mode. The module is 3 pure functions with full boundary tests. |
| R7 | pr-test-analyzer | 4 | No negative test for malformed `KARTOVA_*` values. | skip — spec keeps validation out of scope (trusted operator input, documented at gate 6). |
| R8 | pr-test-analyzer | nit | "401 marks the attempt" test calls the handler without `act()`. | skip — that path makes no state update, so there is no act warning. |
| — | code-reviewer | — | No new Critical/Important issues. Verified the 1e954d8 fixes (regex, using, docs). | — |
| — | pr-test-analyzer | — | All 5 plan Review Focus items are genuinely pinned. | — |
