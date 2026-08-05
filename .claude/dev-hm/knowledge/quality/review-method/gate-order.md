# Review method — Gate order

Section of `knowledge/quality/review-method.md`.


The canonical pipeline order, cheapest-first so expensive signals run on code already known to
be well-formed (QUA-001 evidence comes from these runs):

1. Format check (no reformat-on-CI; formatting is the author's job)
2. Lint + static analysis (QUA-025, QUA-027)
3. Build / compile + type check (QUA-001)
4. Unit tests (QUA-002)
5. Coverage computation on changed lines (QUA-010)
6. Integration tests, sanitizer runs for native stacks (SEC-101)
7. Dependency audit when manifests changed (SEC-062)
8. E2E/system suites, scheduled or on release branches

A gate that is red stops the pipeline; downstream results on top of a red gate are noise. Local
pre-handoff runs mirror at least stages 1–5 — the self-check is not allowed to outsource them
to CI and hope.
