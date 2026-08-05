# C# review checklist — Resource management {#resource-management}

Section of `knowledge/csharp/review-checklist.md`.


| Check | Severity | Notes |
|---|---|---|
| `IDisposable`/`IAsyncDisposable` correctness | S1 | Types owning disposable fields implement dispose and release them; locals use `using`/`await using`. Oracle QUA-CS-009 |
| No per-call `HttpClient` | S1 | Use `IHttpClientFactory`/typed clients; a raw `new HttpClient()` per request exhausts sockets |
| `DbContext`/connection scoped correctly | S1 | Not captured by a singleton, not shared across threads, not held past its unit of work |
| Streams/readers disposed | S2 | `using` on file/network streams, `SqlConnection`, readers |
| Event unsubscription | S2 | Handlers unsubscribed when the subscriber's lifetime ends; long-lived publisher + short-lived subscriber leaks |
