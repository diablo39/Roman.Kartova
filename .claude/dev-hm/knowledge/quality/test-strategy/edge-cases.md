# Test strategy — Edge cases

Section of `knowledge/quality/test-strategy.md`.


For each new input-processing function, cover (QUA-004):

| Class | Cases |
|---|---|
| Emptiness | empty string/collection, null/None/undefined, missing field, whitespace-only |
| Boundaries | zero, negative, min/max of the type, off-by-one at each documented limit, empty vs single vs many |
| Invalid | wrong type, malformed encoding, out-of-range enum, duplicate keys, contradictory fields |
| Size | oversized payloads at and above the configured limit (also SEC-052) |
| Text | non-ASCII, combining characters, RTL text, embedded NUL and control characters |
| Time | DST transitions, leap day, epoch boundaries, timezone-naive vs aware mixing |
| Concurrency | two simultaneous calls on the same entity where the diff touches shared state |

Excluding a class is legitimate when the case cannot occur — say so in the handoff with the
reason ("callers are internal and validated upstream at X"), which converts the gap from
unknown to reviewed. Property-based testing (proptest, Hypothesis, fast-check, jqwik) covers
these classes mechanically for pure functions; per-stack guidance in the testing files.
