# C# testing — NSubstitute {#nsubstitute}

Section of `knowledge/csharp/testing.md`.


Substitute interfaces (and virtual members); design code to depend on abstractions so this is
possible.

```csharp
var repo = Substitute.For<IOrderRepository>();
repo.GetAsync(42, Arg.Any<CancellationToken>()).Returns(new Order(42));

var sut = new OrderService(repo);
var order = await sut.LoadAsync(42, CancellationToken.None);

Assert.AreEqual(42, order.Id);
await repo.Received(1).GetAsync(42, Arg.Any<CancellationToken>());
```

| Need | NSubstitute API |
|---|---|
| Return a value | `.Returns(x)`, `.Returns(a, b, c)` for successive calls |
| Async return | `.Returns(Task.FromResult(x))` or just `.Returns(x)` on a `Task<T>` member |
| Throw | `.Returns(x => throw new …)` or `.ThrowsAsync(...)` |
| Argument match | `Arg.Any<T>()`, `Arg.Is<T>(predicate)` |
| Verify called | `repo.Received(n).Method(...)`, `.DidNotReceive()` |
| Capture arguments | `Arg.Do<T>(x => captured = x)` |

Enable `NSubstitute.Analyzers.CSharp` — it flags non-virtual substitution mistakes at compile time.
Substitute your own abstractions, not framework types you do not own; wrap those behind an
interface first (see `knowledge/csharp/platform.md`). Do not substitute `TimeProvider` — use the
built-in `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
