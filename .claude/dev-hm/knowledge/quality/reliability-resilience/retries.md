# Reliability and resilience — Retries

Section of `knowledge/quality/reliability-resilience.md`.


The control: retries are bounded, backed off, and idempotent — all three visible in code or
config (QUA-080). Retry only failures that are plausibly transient: connection failures,
timeouts, 5xx and 429 responses; never business rejections (4xx), which will fail identically
every time. Bound the attempts (two or three is usually right), use exponential backoff with
full jitter so synchronized clients don't stampede a recovering dependency, cap the total retry
time inside the caller's deadline, and honor Retry-After when the server sends one.

Idempotency is the precondition, not an afterthought: a retry is safe only when running the
operation twice has the effect of running it once. Reads and full-state writes are idempotent by
nature; creation and side-effecting operations are made idempotent with idempotency keys — the
client generates a unique key per logical operation, the server stores the outcome keyed by it,
and a duplicate arrival replays the stored outcome instead of re-executing (the Idempotency-Key
request-header convention used by payment APIs; the dedup store needs a TTL and the key scopes
to one operation). No key, no retry on non-idempotent operations.

Retry at one layer only. When the platform or service mesh already retries, application code
does not stack its own attempts on top — layered retries multiply (three times three is nine
calls per failure). Where call volume is high, add a retry budget: cap retries to a small
fraction of total traffic so retries can never become a self-inflicted flood. Prefer the stack's
standard resilience library (resilience4j for Java, Polly and the built-in HTTP resilience
handlers for .NET, tenacity for Python, cockatiel or similar for Node, tower middleware for
Rust) over hand-rolled loops — the libraries get jitter, deadlines, and metrics right.

Verification: three tests per retried operation. Transient recovery: the stub fails twice then
succeeds — the operation succeeds and the stub saw exactly the expected number of calls.
Persistent failure: the stub always fails — the operation gives up after the maximum attempts,
within the total-time cap. Idempotent replay: the same idempotency key submitted twice produces
exactly one side effect — assert the effect count in the store, not just the response status.
