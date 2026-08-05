# Test adequacy — Mutation

Section of `knowledge/quality/test-adequacy.md`.


Mutation testing measures adequacy directly: a tool applies small code changes (invert a
condition, drop a statement, change a constant), runs the suite against each mutant, and
reports the mutants that survived. A surviving mutant is a behavior change no test noticed —
the exact shape of a future regression. This is the mechanical form of P5 detection: a
worn-out suite shows up as a falling mutation score long before it shows up as a production
incident.

Established tools per stack (versions deliberately unpinned; stack testing files carry
specifics):

| Stack | Tool |
|---|---|
| TypeScript / JavaScript | Stryker (StrykerJS) |
| C# / .NET | Stryker.NET |
| Java | PIT (pitest) |
| Python | mutmut |
| Rust | cargo-mutants |
| C++ | mull |
| Dart / Flutter | no de-facto standard tool; rely on the spot-check discipline below |

Running it affordably, so it happens at all:

- Full-project runs are batch jobs (hours on a large codebase) — schedule them, don't gate
  per-diff on them. Current tools make targeted runs cheap: incremental modes reuse prior
  results and re-test only mutants whose code or tests changed; diff scoping (`--since`,
  changed-file filters) restricts mutation to the change; concurrency matched to cores
  parallelizes the rest.
- The per-diff instrument stays the QUA-014 spot-check: for each new branch enforcing a
  protective control mapped under SEC-130 — authorization, payment, and data deletion are the
  canonical examples — invert the condition mentally or in a scratch run and name the test
  that fails. It costs minutes and needs no tooling.
- Full tool runs pay off in three places: critical modules (the QUA-014 class, run with a
  configured threshold), suites suspected of being green-but-toothless (high coverage, bugs
  still escaping), and periodic suite-health checks on a schedule.
- When the repository configures a mutation tool and threshold, changes to critical modules
  run it and record the score in the handoff — that is what the oracle's mutation entry
  checks. Treat the threshold like the coverage floor: a configured project value, enforced
  where configured, not a universal number.

Interpreting survivors: each surviving mutant is a missing test (write it), dead code (delete
it), or an equivalent mutant — a change with no observable effect (mark it ignored with a
reason). Chasing a 100% kill rate burns time on equivalents; investigating every survivor on
a critical module is the point.
