# Review method — Error handling review

Section of `knowledge/quality/review-method.md`.


Backs QUA-030 – QUA-034. Walk every catch/except/error-branch in the diff and ask four
questions: (1) Is the failure handled or propagated — never silently dropped (QUA-030)?
(2) Does wrapping preserve the original cause and stack (QUA-031)? (3) Is every error-shaped
return value consumed or explicitly discarded with a reason (QUA-032)? (4) Do public entry
points validate arguments and fail fast with a specific error (QUA-033)? For request handlers,
check the status mapping: validation and authorization failures are client errors (4xx),
internal faults are 5xx, and no endpoint reports success with an error payload (QUA-034) —
callers and monitors alike depend on that contract. Catch the narrowest exception type the
recovery actually handles; `catch (Exception)` that logs-and-continues turns every future bug
into silent corruption. The oracle entries decide only what is observable — QUA-030 counts
retry, fallback, and propagate-with-context as handling (logging alone is not); QUA-033 checks
that rejection errors are typed. Whether a chosen recovery is the right one, or an error message
specific enough, is judgment: report it as a `finding` with a severity, not as an oracle
verdict. Security-path error handling has stricter rules (fail closed,
SEC-070/071) — see `knowledge/security/secure-coding-review.md#exceptions-and-fail-closed-behavior`.
