# Async and concurrency

| Sev | Finding | What to look for |
|---|---|---|
| S1 | Race on shared mutable state | Overlapping async writes to a shared variable/ref; guard with a single in-flight promise or a request key. |
| S2 | Independent awaits serialized | Sequential `await`s with no data dependency; use `Promise.all` (and `Promise.allSettled` when partial failure is acceptable). |
| S2 | No cancellation on unmount/refetch | `fetch` in an effect without an `AbortController` tied to cleanup, letting a late response set state on an unmounted component. |
