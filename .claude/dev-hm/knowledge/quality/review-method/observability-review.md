# Review method — Observability review

Section of `knowledge/quality/review-method.md`.


Backs QUA-040 – QUA-042. New code is operable when someone can tell, at 3 a.m., what it did and
why. Logging goes through the project logger with levels meaning something: error = someone
should act, warn = degraded but coping, info = state change worth an audit trail, debug = off in
production. Leftover print/console.log debug output fails QUA-040; whether a chosen level is
appropriate is judgment — report it as a `finding`, not a QUA-040 verdict. Every new outbound call sets
an explicit timeout and logs its failure path with enough context to identify the operation and
correlation ID (QUA-041) — a missing timeout is a future outage, and reviewers treat "the
library default handles it" as fail unless the default is explicit in config. Where the repo has
metrics/tracing conventions, new endpoints and jobs join them (QUA-042): spans propagated,
request counters/durations registered. Log content rules (no secrets, no PII, injection-safe)
are the security oracle's SEC-021/081/082.
