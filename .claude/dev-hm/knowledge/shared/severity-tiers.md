# Severity tiers

Every oracle entry and review finding carries exactly one severity tier. The tier decides what
happens at the gate — not the reviewer's mood, not negotiation in the moment.

| Tier | Name | Meaning | Gate consequence |
|---|---|---|---|
| S0 | Block | Exploitable security flaw, data-loss risk, or a deliverable that does not build or whose tests fail | Work is not done. Not waivable by anyone. |
| S1 | Must-fix | Serious defect that will bite in production or materially weakens security/quality posture | Work is blocked until fixed, or waived by the owning gate agent with recorded rationale |
| S2 | Should-fix | Real defect with limited blast radius, or a violated norm with a working result | Fix in this change, or waive with recorded rationale |
| S3 | Advisory | Improvement opportunity; style beyond linter scope; future-proofing | Does not gate. Recorded in the report; the developer decides |

## Definitions and examples

### S0 — Block

The change must not ship as-is under any circumstance. Examples:

- SQL built by string concatenation with request input (SEC-001).
- Endpoint reachable without authentication that reads or writes user data (SEC-010).
- Hardcoded production credential in the diff (SEC-020).
- Certificate validation disabled on an outbound TLS connection (SEC-031).
- Build fails or the test suite is red on the handed-off change (QUA-001, QUA-002).
- Authorization check that fails open — an exception path grants access (SEC-071).

### S1 — Must-fix

Will cause incidents or unacceptable risk; fixing it is the default. Examples:

- Missing tests for new business logic (QUA-003); coverage of changed lines below the floor (QUA-010).
- Outbound request target taken from external input with no allowlist validation (SEC-090).
- Broken cipher or hash chosen for a security purpose (SEC-030).
- Swallowed exception on an error path that leaves state undefined (QUA-030).
- Unbounded cache or queue introduced (QUA-072).

### S2 — Should-fix

Worth fixing now while context is fresh; a waiver is acceptable when cost clearly exceeds risk.
Examples: error message leaking internal paths to callers (SEC-072), sleep-based test
synchronization (QUA-012), duplicated logic block (QUA-021), missing doc comment on a new public
API (QUA-050).

### S3 — Advisory

Signal, not a gate. Examples: magic numbers that could be named constants (QUA-024),
performance claim without a measurement (QUA-073), missing tracing on a repo that has tracing
conventions (QUA-042).

## Assigning severity

- The oracle entry's severity is the default. Findings outside any oracle entry get a tier from
  the definitions above, cited as `finding` instead of an oracle ID.
- A verifier (reviewer or gate) may raise a severity with one line of justification — context can
  make a default-S2 issue an S1 (e.g. the duplicated block is an authorization check).
- No role lowers a severity in place. Lowering is a waiver, and goes through the waiver flow.

## Waiver rules

| Severity | Who may waive | Requirements |
|---|---|---|
| S0 | No one | — |
| S1, S2 on SEC-* entries and security findings | senior-security-engineer only | Recorded rationale; waiver line in the gate report |
| S1, S2 on QUA-* entries and quality findings | senior-quality-engineer only | Recorded rationale; waiver line in the gate report |
| S3 | No waiver needed | Developer decides; disagreement may be noted in the handoff |

Waiver line format (part of the shared verdict format, see
`knowledge/shared/defense-in-depth.md`):

```
SEC-060 waived tools/gen/lockfile — generated throwaway fixture, not shipped (accepted by senior-security-engineer)
```

A waiver applies to one oracle ID at one location in one change. It does not carry over to the
next change touching the same code; the entry is re-evaluated each time.
