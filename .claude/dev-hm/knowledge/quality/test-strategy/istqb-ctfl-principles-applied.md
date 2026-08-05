# Test strategy — ISTQB CTFL principles applied

Section of `knowledge/quality/test-strategy.md`.


The seven testing principles (ISTQB CTFL; current edition per `knowledge/shared/versions.md`),
each with its operational consequence in this plugin:

| # | Principle | Consequence here |
|---|---|---|
| P1 | Testing shows the presence of defects, not their absence | A green suite is evidence, not proof; verdicts say "tests pass", never "no bugs" |
| P2 | Exhaustive testing is impossible | Prioritize by risk: edge cases, security-relevant branches, money/data paths (QUA-004, QUA-014) |
| P3 | Early testing saves time and money | Tests ship in the same diff as the code (QUA-003); the self-check runs before handoff, not after the gate |
| P4 | Defects cluster together | Every bug fix carries a regression test (QUA-005); a buggy module earns extra tests, not just the one fix |
| P5 | Tests wear out | Repeated identical tests stop finding new defects; extend and vary tests when code changes; mutation spot-checks (QUA-014) detect worn-out suites |
| P6 | Testing is context dependent | Coverage floors and level mix are policy defaults; repos may configure stricter rules, which win |
| P7 | Absence-of-errors fallacy | A fully tested wrong feature is still wrong; acceptance checks in work packages trace to requirements, not just to code |

ISTQB CTFL names five test levels: component, component integration, system, system
integration, and acceptance. The pyramid's "unit / integration / E2E" maps onto the first,
second-plus-third, and acceptance levels respectively.
