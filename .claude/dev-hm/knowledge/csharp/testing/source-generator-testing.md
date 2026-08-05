# C# testing — Source-generator testing {#source-generator-testing}

Section of `knowledge/csharp/testing.md`.


Test generators with the Roslyn testing harness (`Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing`),
asserting on both emitted sources and reported diagnostics.

```csharp
[TestMethod]
public async Task Generates_mapper_for_annotated_partial()
{
    await new CSharpSourceGeneratorTest<MapperGenerator, DefaultVerifier>
    {
        TestState =
        {
            Sources = { InputSource },
            GeneratedSources =
            {
                (typeof(MapperGenerator), "UserDto.g.cs", ExpectedSource),
            },
        },
    }.RunAsync();
}

[TestMethod]
public async Task Reports_diagnostic_when_target_not_partial()
{
    await new CSharpSourceGeneratorTest<MapperGenerator, DefaultVerifier>
    {
        TestState =
        {
            Sources = { NonPartialSource },
            ExpectedDiagnostics = { DiagnosticResult.CompilerError("MAP001").WithSpan(2, 14, 2, 21) },
        },
    }.RunAsync();
}
```

Cover: expected generated code, each diagnostic (id, span, arguments), and no-op cases (target
absent → no output). Add integration tests that compile and run the generated code, and a
BenchmarkDotNet comparison against the reflective baseline when the point of the generator is
performance. Verify incrementality by asserting cached pipeline steps do not re-run on unrelated
edits.
