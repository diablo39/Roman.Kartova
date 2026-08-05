# Quality oracle (QUA-*) — index

Language-agnostic quality checks applied to every code change by three roles in sequence:
developer self-check, reviewer verification, quality gate. Stack-specific QUA-<code>-NNN entries
live in `oracles/addenda/<stack>.md`.

Diff scope, determinism, verdict grammar and severity tiers come from your own prompt. This file
adds only what is specific to running this oracle: IDs are stable and never reused, and where a
core entry and a stack-addendum entry cover the same defect, the stricter severity governs.

## Section routing

Read **only** the section files whose trigger the diff activates.

| Section | File | IDs | Read when the diff touches |
|---|---|---|---|
| Correctness | `oracles/quality/correctness.md` | QUA-001–QUA-006 | **always** — build, tests, new behavior, bug fixes, refactors |
| Test adequacy | `oracles/quality/test-adequacy.md` | QUA-010–QUA-113 | any diff adding or changing tests, or adding testable behavior |
| Maintainability | `oracles/quality/maintainability.md` | QUA-020–QUA-027 | **always** — structure, duplication, complexity, dead code, lint |
| Error handling | `oracles/quality/error-handling.md` | QUA-030–QUA-034 | catch/except blocks, error returns, precondition checks, request handlers |
| Observability | `oracles/quality/observability.md` | QUA-040–QUA-045 | logging, outbound calls, new endpoints or background jobs, tracing |
| Documentation | `oracles/quality/documentation.md` | QUA-050, QUA-051, QUA-052, QUA-053 | new public API, config keys, structural decisions, user-facing behavior |
| Dependency hygiene | `oracles/quality/dependencies.md` | QUA-060, QUA-061, QUA-062, QUA-063 | manifest/lockfile changes, new third-party packages, generated files |
| Performance budgets | `oracles/quality/performance.md` | QUA-070–QUA-074 | loops issuing I/O, hot paths, caches/queues/buffers, perf claims |
| Reliability and resilience | `oracles/quality/resilience.md` | QUA-080, QUA-081, QUA-082 | retry logic, new hard dependency on an external service |
| Migration safety | `oracles/quality/migrations.md` | QUA-090–QUA-094 | schema changes, data backfills, new required columns |
| Release readiness | `oracles/quality/release.md` | QUA-100, QUA-101, QUA-102, QUA-112 | version bumps, release branches, deploy config, feature flags |
| Accessibility | `oracles/quality/accessibility.md` | QUA-110, QUA-111 | new or changed interactive UI elements |

## Read budget

Correctness and Maintainability are always read; a typical feature diff activates **3–5** more.
Opening more than 8 means the scope statement is wrong. Stack addenda are read whole.

Report unopened sections as `n/a` summary counts. Never guess a verdict for a section you did not
open — an unopened section is `n/a`, not `pass`.
