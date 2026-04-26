# Test Pattern Reference

Use the section matching the detected project stack and available libraries.

## 1. Parameterized Test Patterns

### C# - xUnit

```csharp
[Theory]
[InlineData("hello", "5d41402abc4b2a76b9719d911017c592")]
[InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
[InlineData("héllo", "fae7e54732e497ea5400821aed803bab")]
public void ComputeMd5_KnownInputs_ReturnsExpectedHash(string input, string expected)
{
    var result = HashHelper.ComputeMd5(input);
    Assert.Equal(expected, result);
}
```

Use `[MemberData]` when the dataset is too large or needs richer object setup.

### C# - NUnit

```csharp
[TestCase("hello", "5d41402abc4b2a76b9719d911017c592")]
[TestCase("", "d41d8cd98f00b204e9800998ecf8427e")]
[TestCase("héllo", "fae7e54732e497ea5400821aed803bab")]
public void ComputeMd5_KnownInputs_ReturnsExpectedHash(string input, string expected)
{
    Assert.AreEqual(expected, HashHelper.ComputeMd5(input));
}
```

### Java - JUnit 5

```java
@ParameterizedTest
@CsvSource({
    "hello, 5d41402abc4b2a76b9719d911017c592",
    "'', d41d8cd98f00b204e9800998ecf8427e",
    "héllo, fae7e54732e497ea5400821aed803bab"
})
void computeMd5_knownInputs_returnsExpectedHash(String input, String expected) {
    assertEquals(expected, HashHelper.computeMd5(input));
}
```

Use `@MethodSource` for complex objects or setup-heavy scenarios.

### JavaScript or TypeScript - Jest or Vitest

```ts
test.each([
  ["hello", "5d41402abc4b2a76b9719d911017c592"],
  ["", "d41d8cd98f00b204e9800998ecf8427e"],
  ["héllo", "fae7e54732e497ea5400821aed803bab"],
])("computeMd5(%s) returns %s", (input, expected) => {
  expect(computeMd5(input)).toBe(expected);
});
```

### Python - pytest

```python
@pytest.mark.parametrize(
    "input_value, expected",
    [
        ("hello", "5d41402abc4b2a76b9719d911017c592"),
        ("", "d41d8cd98f00b204e9800998ecf8427e"),
        ("héllo", "fae7e54732e497ea5400821aed803bab"),
    ],
)
def test_compute_md5_known_inputs(input_value, expected):
    assert compute_md5(input_value) == expected
```

## 2. Assertion Guidance

Prefer these:

- Exact scalar equality
- Exact collection contents and order when order matters
- Exact exception type and meaningful message content
- Exact state-transition results
- Exact numeric values, with narrow tolerance only when floating-point behavior requires it
- A short comment above the assertion block when a business rule, audit requirement, or contract invariant explains why the exact value matters

Avoid these as the primary oracle:

- Not-null only
- Not-empty only
- Length-only
- Type-only
- "does not throw" as the only proof of correctness
- Comments that only restate the next assertion without adding domain meaning

### .NET-Specific Assertion Patterns

**Exception assertions (xUnit):**
```csharp
var ex = await Assert.ThrowsAsync<ArgumentException>(
    () => sut.ProcessAsync(invalidInput));
Assert.Equal("Value cannot be negative. (Parameter 'amount')", ex.Message);
Assert.Equal("amount", ex.ParamName);
```

**Collection assertions (xUnit):**
```csharp
// Exact ordered contents
Assert.Equal(new[] { "A", "B", "C" }, result.Select(x => x.Name));

// Unordered contents
Assert.Equal(
    expected.OrderBy(x => x.Id),
    actual.OrderBy(x => x.Id));

// Single matching element
var item = Assert.Single(result, x => x.Id == targetId);
Assert.Equal(expectedName, item.Name);
```

**NSubstitute verification tied to behavior:**
```csharp
// Good: verify the call happened with exact args tied to the test scenario
await dependency.Received(1).SaveAsync(
    Arg.Is<Order>(o => o.Amount == 100m && o.Status == OrderStatus.Pending),
    Arg.Any<CancellationToken>());

// Bad: just verify call count without checking what was passed
await dependency.Received(1).SaveAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
```

**Temporal assertions with TimeProvider:**
```csharp
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
var sut = new ExpiryChecker(fakeTime);

Assert.False(sut.IsExpired(deadline)); // before deadline
fakeTime.SetUtcNow(deadline.AddSeconds(1));
Assert.True(sut.IsExpired(deadline)); // after deadline
```

**Negative assertions (verify what should NOT happen):**
```csharp
// Verify side effect did NOT occur for invalid input
await sut.TryProcessAsync(invalidInput, CancellationToken.None);
await store.DidNotReceive().ApplyAsync(Arg.Any<PositionEntry>());
```

When you add rationale comments, keep them concise and group-level. Prefer:

```csharp
// Audit trail requirement: updates must record the acting user
Assert.Equal(userId, entity.UpdateUser);
Assert.Equal(userId, entity.ModifiedBy);
```

Avoid:

```csharp
// UpdateUser should equal userId
Assert.Equal(userId, entity.UpdateUser);
// ModifiedBy should equal userId
Assert.Equal(userId, entity.ModifiedBy);
```

## 3. Boundary Value Checklist

Use the smallest boundary set that clearly proves behavior.

### Numeric patterns

| Pattern | Values to try |
|---|---|
| Sign boundary | `-1`, `0`, `1` |
| Inclusive threshold `x >= N` | `N-1`, `N`, `N+1` |
| Exclusive threshold `x > N` | `N-1`, `N`, `N+1` |
| Range `[lo, hi]` | `lo-1`, `lo`, `hi`, `hi+1` |
| Limits | min value, max value |

### String patterns

| Pattern | Values to try |
|---|---|
| Empty | `""` |
| Whitespace | `" "`, `"\t"`, `"\n"` |
| Single char | `"a"` |
| Non-ASCII | `"héllo"`, `"日本語"`, `"emoji 🎉"` |
| Null | language-appropriate null value |

### Collection patterns

| Pattern | Values to try |
|---|---|
| Empty | `[]` |
| Single element | `[x]` |
| Typical set | `[a, b, c]` |
| Duplicates | `[a, a, b]` |
| Out-of-range index use | first valid, last valid, first invalid |

## 4. MC/DC Construction

MC/DC requires showing that each atomic condition can independently change the decision outcome.

### Example: `A && B && C`

| Test | A | B | C | Decision | Purpose |
|---|---:|---:|---:|---:|---|
| T1 | T | T | T | T | baseline |
| T2 | F | T | T | F | A independently flips outcome |
| T3 | T | F | T | F | B independently flips outcome |
| T4 | T | T | F | F | C independently flips outcome |

### Example: `A || B`

| Test | A | B | Decision | Purpose |
|---|---:|---:|---:|---|
| T1 | F | F | F | baseline |
| T2 | T | F | T | A independently flips outcome |
| T3 | F | T | T | B independently flips outcome |

Translate truth values into concrete inputs using real boundaries whenever possible.

Examples:

- `amount > 0` -> use `-1`, `0`, `1`
- `dueDate <= cutoff` -> use day before, exact cutoff, day after
- `name != null && name.Length > 0` -> use `null`, `""`, `"x"`

## 5. Property-Based Testing

Use property-based tests only if the project already includes the needed library or has an obvious accepted place for it.

Supported examples if libraries already exist:

- Python: Hypothesis
- JavaScript or TypeScript: fast-check
- C#: FsCheck
- Java: jqwik

Good property candidates:

- Roundtrip: `decode(encode(x)) == x`
- Idempotence: `normalize(normalize(x)) == normalize(x)`
- Commutativity: `f(a, b) == f(b, a)`
- Ordering or monotonic invariants
- Reference implementation comparison when a trusted simple oracle exists

Fallback rule:

- If the property-based library is absent, translate the same invariant into a small, explicit parameterized matrix instead of adding a dependency silently.

## 6. Model-Based Testing

Use model-based tests only when one of these is true:

- The code clearly represents a state machine or workflow
- Existing tests already use a state-table or scenario-matrix pattern
- The transition space is small enough to encode explicitly without introducing a new framework

A minimal model-based structure should cover:

1. Initial state
2. Valid transition sequence
3. Invalid transition sequence
4. Final observable state

Example workflow assertions:

- Draft -> Submitted -> Approved reaches Approved
- Draft -> Approved without submission is rejected
- Approved -> Cancelled is rejected if business rules forbid it

If no model-based tooling exists, encode transitions as parameterized example-based tests instead of adding infrastructure.

## 7. Mutation-Killing Heuristics

Use the smallest divergence input that clearly distinguishes original from mutant.

| Mutation shape | Best target input |
|---|---|
| `>` -> `>=` or `<` -> `<=` | exact threshold |
| `>=` -> `>` or `<=` -> `<` | exact inclusive boundary |
| `&&` -> `||` | case where only one clause is true |
| `||` -> `&&` | case where one clause should be sufficient |
| `+` -> `-` or `*` -> `+` | small exact integers with obvious expected result |
| `true` return -> `false` return | simplest input that should unambiguously succeed |

When multiple mutants exist for one method, prefer a parameterized structure if the assertions and setup remain readable.

## 8. .NET Infrastructure Testing Patterns

### EF Core InMemory for Repository Tests

```csharp
private static DbContextOptions<GuardRailDbContext> CreateInMemoryOptions()
    => new DbContextOptionsBuilder<GuardRailDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

[Fact]
public async Task GetByUnitAsync_ReturnsOrderedResults()
{
    // Arrange
    var options = CreateInMemoryOptions();
    await using var ctx = new GuardRailDbContext(options);
    ctx.LimitDefinitions.AddRange(
        new LimitDefinition { UnitId = 1, LimitType = LimitType.Collateral, ModuleName = "B" },
        new LimitDefinition { UnitId = 1, LimitType = LimitType.Collateral, ModuleName = "A" });
    await ctx.SaveChangesAsync();
    var sut = new LimitDefinitionRepository(ctx);

    // Act
    var result = await sut.GetByUnitAsync(1, CancellationToken.None);

    // Assert — verify exact order from repository contract
    Assert.Equal(2, result.Count);
    Assert.Equal("A", result[0].ModuleName);
    Assert.Equal("B", result[1].ModuleName);
}
```

### IDbContextFactory Mock (for singleton services)

```csharp
var options = CreateInMemoryOptions();
var factory = Substitute.For<IDbContextFactory<GuardRailDbContext>>();
factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
    .Returns(_ => Task.FromResult(new GuardRailDbContext(options)));
var sut = new MySingletonService(factory);
```

### IAsyncEnumerable Testing

```csharp
[Fact]
public async Task ProcessStream_ConsumesAllItems()
{
    // Arrange
    var items = new[] { new Trade { Id = 1 }, new Trade { Id = 2 } };
    mockRepo.GetTradesAsync(Arg.Any<CancellationToken>())
        .Returns(items.ToAsyncEnumerable());

    // Act
    await sut.ProcessStreamAsync(CancellationToken.None);

    // Assert — verify all items were processed
    await store.Received(2).ApplyAsync(Arg.Any<PositionEntry>());
}

// Helper to create IAsyncEnumerable from array
public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
{
    foreach (var item in source)
    {
        yield return item;
        await Task.Yield();
    }
}
```

### ConcurrentDictionary / Thread-Safety Testing

```csharp
[Fact]
public async Task ConcurrentAccess_DoesNotLoseUpdates()
{
    // Arrange
    var sut = new GroupAmountStore();
    var groupId = 42;
    var tasks = Enumerable.Range(0, 100)
        .Select(i => Task.Run(() => sut.Apply(new PositionEntry { GroupId = groupId, Amount = 1m })));

    // Act
    await Task.WhenAll(tasks);

    // Assert — all 100 applies should be reflected
    Assert.Equal(100m, sut.GetAmount(groupId));
}
```

### BackgroundService Lifecycle Testing

```csharp
[Fact]
public async Task ExecuteAsync_CancellationStopsGracefully()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    var sut = new MyBackgroundService(mockDep);

    // Act
    await sut.StartAsync(cts.Token);
    cts.Cancel();
    await sut.StopAsync(CancellationToken.None);

    // Assert — verify graceful shutdown behavior
    await mockDep.Received().CleanupAsync(Arg.Any<CancellationToken>());
}
```

### Dispose / AsyncDispose Testing

```csharp
[Fact]
public async Task Dispose_ReleasesResources()
{
    // Arrange
    var sut = new ResourceHolder(mockResource);

    // Act
    await sut.DisposeAsync();

    // Assert
    mockResource.Received(1).Release();
    Assert.Throws<ObjectDisposedException>(() => sut.DoWork());
}
```
