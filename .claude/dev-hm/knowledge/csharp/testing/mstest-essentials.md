# C# testing — MSTest essentials

Section of `knowledge/csharp/testing.md`.


Test projects reference `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`, and
`Microsoft.NET.Test.Sdk` (the VSTest runner — these projects do **not** opt into the Microsoft
Testing Platform runner, so `dotnet test` works without extra flags). Put the namespace in a global
using so test files stay clean:

```xml
<ItemGroup>
  <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
</ItemGroup>
```

| Concept | MSTest mechanism |
|---|---|
| Test class | `[TestClass]` on a `sealed` class |
| Test method | `[TestMethod]` |
| Parameterized test | `[TestMethod]` + `[DataRow(...)]` (or `[DynamicData]`); `[DataTestMethod]` is legacy — not needed since MSTest 3 |
| Per-test setup/teardown | `[TestInitialize]` / `[TestCleanup]` |
| Per-class setup/teardown | `[ClassInitialize]` (static, takes `TestContext`) / `[ClassCleanup]` |
| Per-assembly setup/teardown | `[AssemblyInitialize]` (static, takes `TestContext`) / `[AssemblyCleanup]` |
| Skip | `[Ignore("reason")]` |
| Serialize execution | `[assembly: DoNotParallelize]` or `[DoNotParallelize]` on a class |

Structure every test as Arrange-Act-Assert with those three phases visible. One logical assertion
per test. MSTest creates a new test-class instance per test method, so instance fields are isolated
— avoid static mutable state, which reintroduces cross-test coupling.

MSTest does **not** parallelize by default; opt in per assembly with
`[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]`. Assemblies that share
one container or one host fixture must stay serial (`[assembly: DoNotParallelize]`).

```csharp
[TestClass]
public sealed class PriceCalculatorTests
{
    [TestMethod]
    [DataRow(100, 0.25, 125)]
    [DataRow(0, 0.25, 0)]
    public void Gross_applies_vat_rate(decimal net, decimal rate, decimal expected)
    {
        var sut = new PriceCalculator();
        var gross = sut.Gross(net, rate);
        Assert.AreEqual(expected, gross);
    }

    [TestMethod]
    public void Gross_rejects_negative_rate()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new PriceCalculator().Gross(100, -1));
}
```

Assertion picks: `Assert.AreEqual` / `AreNotEqual`, `Assert.AreSame`, `Assert.IsTrue` / `IsFalse`,
`Assert.IsNull` / `IsNotNull`, `Assert.IsInstanceOfType<T>` (prefer over a hard cast),
`Assert.Contains`, `CollectionAssert.AreEquivalent` / `AreEqual` for sequences, `StringAssert` for
substring and pattern checks, and `Assert.ThrowsExactly<T>` / `await Assert.ThrowsExactlyAsync<T>`
for exceptions. Prefer the `Exactly` forms — `Assert.Throws<T>` also accepts derived types and will
pass on the wrong exception. For async, return `Task` from the test and `await` — never block.
`MSTest.Analyzers` flags most misuse (wrong assertion overloads, missing `[TestClass]`, async voids)
at compile time; keep it enabled.
