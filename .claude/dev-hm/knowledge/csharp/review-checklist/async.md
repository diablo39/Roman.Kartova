# C# review checklist — Async and concurrency {#async}

Section of `knowledge/csharp/review-checklist.md`.


| Check | Severity | Notes |
|---|---|---|
| No sync-over-async | S1 | No `.Result`, `.Wait()`, `GetAwaiter().GetResult()` outside a documented top-level boundary. Oracle QUA-CS-006 |
| No `async void` | S1 | Except event handlers matching the framework signature. Oracle QUA-CS-005 |
| `CancellationToken` propagated | S2 | Public async methods accept and forward a token to downstream async calls. Oracle QUA-CS-007 |
| `ConfigureAwait` policy consistent | S2 | One policy per assembly; library code either uses `ConfigureAwait(false)` uniformly or documents why not. Oracle QUA-CS-008 |
| No lock held across `await` | S1 | Use `SemaphoreSlim.WaitAsync` for async mutual exclusion; a `lock` around `await` deadlocks |
| Thread-safe shared state | S1 | Mutable state touched by concurrent tasks is synchronized or immutable; no non-thread-safe singletons |
| No fire-and-forget without handling | S2 | Unawaited tasks capture exceptions silently; await, or observe with a logged continuation |
