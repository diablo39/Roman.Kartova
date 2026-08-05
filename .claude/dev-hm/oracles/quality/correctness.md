# Quality oracle — Correctness

Section of `oracles/quality-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-001 | Build passes | The full build (compiler plus type checker where the stack has one) runs clean on the change; command and result recorded in the handoff | S0 | ISO functional suitability | knowledge/quality/review-method/gate-order.md |
| QUA-002 | Tests pass | The relevant test suite was executed and is green; zero tests skipped, disabled, or weakened to reach green; command and pass/fail counts recorded | S0 | ISO functional suitability · P1 | knowledge/quality/test-strategy/ci-test-gates.md |
| QUA-003 | New behavior tested | Every new public function, endpoint, or business-logic branch in the diff is exercised by at least one test asserting its observable output or effect | S1 | P1, P3 | knowledge/quality/test-strategy/what-to-test-at-which-level.md |
| QUA-004 | Edge cases enumerated | For each new function in the diff that parses or validates external input (request parameters/bodies, headers, files, messages, CLI arguments), tests cover empty/null, boundary, and invalid inputs — or the handoff lists the excluded case with the reason it cannot occur | S1 | P2 | knowledge/quality/test-strategy/edge-cases.md |
| QUA-005 | Regression test with fix | Every bug fix includes a test that fails on the pre-fix code and passes on the fixed code; the handoff records that both states were observed | S1 | P4 | knowledge/quality/test-strategy/what-to-test-at-which-level.md |
| QUA-006 | Refactor behavior parity | A change described as a refactor modifies no test assertions and no public contracts; the pre-existing test suite passes unmodified, and the run is recorded | S1 | ISO functional suitability | knowledge/quality/test-strategy/what-to-test-at-which-level.md |
