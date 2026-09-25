# Modern .NET platform patterns — Async and cancellation

Section of `knowledge/csharp/platform.md`.


- Async all the way down; do not block on async with `.Result`, `.Wait()`, or
  `GetAwaiter().GetResult()` outside a documented top-level sync boundary.
- Public async methods accept a `CancellationToken` and forward it to every downstream async call
  that accepts one.
- Use `async void` only for event handlers whose signature the framework requires; everything else
  returns `Task`/`Task<T>`/`ValueTask`.
- Library code that does not touch a synchronization context can use `ConfigureAwait(false)`; apply
  one policy consistently across an assembly rather than mixing. ASP.NET Core has no
  synchronization context, so app code there does not need it.
- Return `IAsyncEnumerable<T>` with `[EnumeratorCancellation]` for streaming; do not buffer large
  sequences into a `List` first.

Composition depth — `ValueTask` consumption rules, `WhenAll` error aggregation, linked
cancellation, channels, streaming backpressure, background work — is in
`knowledge/csharp/async-patterns.md`.
