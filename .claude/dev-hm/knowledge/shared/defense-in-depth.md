# Defense in depth: the three-layer gate workflow

Every code change passes the same oracle three times, applied by three roles with increasing
independence. The oracle indexes are `oracles/security-oracle.md` (SEC-*) and
`oracles/quality-oracle.md` (QUA-*); each routes to per-section files under `oracles/security/`
and `oracles/quality/`. Per-stack addenda live in `oracles/addenda/` (SEC-<code>-*, QUA-<code>-*)
and are read whole. Where a core entry and an addendum entry cover the same defect, the stricter
severity and verdict govern.

**Read only the sections the diff activates.** Each index carries a routing table (section →
trigger) and a read budget. Every layer routes independently off that table — a layer never
inherits the section list from the layer before it. Sections not opened are reported as `n/a`
summary counts, never as `pass`.

```mermaid
sequenceDiagram
    participant D as Developer agent
    participant R as Code reviewer
    participant G as Security/Quality gate
    D->>D: implement, then self-check (applicable SEC/QUA + addendum)
    D->>R: handoff: diff + self-check verdict table
    R->>R: independently re-run oracle on the diff (no trust in self-check)
    R->>G: review report: per-ID verdicts + findings
    G->>G: full oracle pass over the change; adjudicate waivers
    G-->>D: fail S0/S1 → back to developer with IDs + remediation pointers
    G-->>G: pass → gate verdict recorded, work item done
```

## Sections

The layer model and the verdict block format are below — everything else is a separate file. Read the section you need, not the whole flow.

| Section | File |
|---|---|
| Layer scoping — what each layer re-runs | `knowledge/shared/defense-in-depth/layer-scoping.md` |
| Escalation and return flow | `knowledge/shared/defense-in-depth/escalation-and-return-flow.md` |
| Waiver flow | `knowledge/shared/defense-in-depth/waiver-flow.md` |
| What travels with the work item | `knowledge/shared/defense-in-depth/what-travels-with-the-work-item.md` |

## The three layers

| Layer | Role | Duty |
|---|---|---|
| 1 Self-check | Developer agent (typescript-senior-dev, senior-python-dev, …) | After implementing, run every applicable core oracle entry plus the stack addendum against the diff. Fix what fails. Attach the verdict table to the handoff. |
| 2 Verification | Language code reviewer (csharp-code-reviewer, …) | Re-run the applicable entries independently — read the code, do not trust the self-check. Report per-ID verdicts plus any findings outside the oracle. |
| 3 Gate | senior-security-engineer (SEC-*), senior-quality-engineer (QUA-*) | Full oracle pass over the change. Adjudicate waiver requests. Issue the gate verdict that marks the work item done or returns it. |
| Durable: control-verification tests | The repository's test suite, run by CI on every change | Automated tests assert each protective control's refusal behavior (`knowledge/security/control-verification-tests.md`). Written at layer 1 alongside the control; verified by the gates (SEC-130 – SEC-134); run on every future change. |

Each layer covers the failure mode of the one before it: the self-check catches most issues at the
cheapest point; the reviewer catches self-check blind spots; the gate catches reviewer drift and
owns the waiver decision. Layers 1–2 may be skipped only when the orchestrating session says so
explicitly for a given work package, with the reason recorded in the work item. Layer 3 — the
security and quality gates — is never skipped for a code change; the only exception is explicit
human approval, recorded in the work item.


## Verdict format

One grammar for all three layers, defined here and nowhere else:

```
<oracle-id|finding> <verdict> [<path>:<line>] — <evidence or rationale>
```

- Verdicts: `pass`, `fail`, `n/a`, `waive-requested` (developer/reviewer), `waived` (gate only).
- `fail`, `waive-requested`, and `waived` lines are itemized, one per line, with location and
  one-sentence evidence. `pass` and `n/a` are reported as summary counts only, per oracle file.
- Findings outside any oracle entry use the literal ID `finding` plus a severity tag.

Report section order, the same for reviewers and gates: (1) a one-line scope statement — what the
change does; (2) the overall verdict with a one-line rationale; (3) the per-oracle-ID verdict
block; (4) findings by severity S0 → S3. Role-specific sections (a gate's threat-model note or
evidence audit) sit between the verdict and the oracle block. The overall verdict takes exactly
one of three values: `pass`, `pass-with-S2s` (no open S0/S1; S2 findings recorded), or `fail`
(any S0, or an S1 without an accepted waiver).

Example report body:

```
Oracle: SEC core 21 pass, 6 n/a; SEC-PY 5 pass, 1 n/a; QUA core 17 pass, 2 fail
QUA-004 fail src/parser.py:52 — no test covers empty input
QUA-010 fail src/parser.py — changed-line coverage 61% (floor 80%), pytest --cov output attached
SEC-060 waive-requested tools/fixtures/lock.json — generated fixture, not shipped
finding S2 src/parser.py:14 — TODO comment references a ticket that is already closed
```

Determinism requirement: two independent runs over the same diff must produce the same verdict
table. Oracle pass criteria are written as observable predicates to make that possible; if a
verdict would depend on taste, the entry does not apply (`n/a` with a reason) or the issue is
reported as a `finding` instead.
