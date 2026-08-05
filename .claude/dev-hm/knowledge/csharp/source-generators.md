# C# source generators

Source generators run at compile time and emit code the compiler treats as part of the project.
They replace runtime reflection with generated, statically-analyzable code: faster, allocation-free
on hot paths, AOT- and trim-safe, and validated at compile time. Package versions are in
`knowledge/shared/versions.md`.

## Decision matrix {#decision}

| Concern | Built-in generator | Reflection cost avoided |
|---|---|---|
| JSON serialization | `System.Text.Json` `JsonSerializerContext` | Metadata discovery per type; AOT/trim breakage |
| Structured logging | `LoggerMessage` methods | Delegate/boxing allocation per call site |
| Configuration binding | Config-binding generator | Reflective property walk at startup |
| Options validation | Options-validation generator | Reflective data-annotation validation |
| Regex | `[GeneratedRegex]` | Runtime pattern compilation on first use |
| Interop / marshalling | `LibraryImport` generator | Reflection-based `DllImport` marshalling |
| Custom mapping/DI/builders | Author an `IIncrementalGenerator` | AutoMapper-style reflection, 10–50× slower |

Reach for a generator when the same reflective work runs repeatedly, on startup, or on an AOT
target. The built-in generators below are the default choice for their concern; a hand-written
generator is warranted only when no built-in covers the pattern (see §custom).

## Built-in generators

### JSON serialization {#json}

Declare a context and serialize through it — no reflection, AOT-safe, ~40% faster startup.

```csharp
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(List<User>))]
internal partial class AppJsonContext : JsonSerializerContext;

var json = JsonSerializer.Serialize(user, AppJsonContext.Default.User);
var back = JsonSerializer.Deserialize(json, AppJsonContext.Default.User);
```

Register the context on ASP.NET Core JSON options
(`options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default)`) so
minimal-API and controller serialization use it. The current LTS lets you set `ReferenceHandler`
directly in `JsonSourceGenerationOptions` for cycle handling.

### Structured logging {#logging}

```csharp
public partial class OrderService(ILogger<OrderService> logger)
{
    [LoggerMessage(EventId = 1400, Level = LogLevel.Information,
        Message = "Order {OrderId} shipped to {Region}")]
    private partial void LogShipped(long orderId, string region);
}
```

Generated methods allocate nothing when the level is disabled and box no value types. Placeholders
in `Message` map to parameters by position. Do not interpolate strings into `ILogger` calls.

### Configuration binding and options validation {#configuration}

The two generators work differently — do not conflate them. The config-binding generator is
interceptor-based: enable it with
`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` and it rewrites
your existing `Bind`/`Get` call sites with reflection-free code; the source stays unchanged. The
options-validation generator is neither interceptor-based nor on by default: you declare a partial
class marked `[OptionsValidator]` implementing `IValidateOptions<T>`, the generator emits its
`Validate` method from the data-annotation attributes on the options type, and you register that
validator in DI. Keep `ValidateDataAnnotations()` until the generated validator is registered —
dropping it with no registered `IValidateOptions<T>` silently disables validation.

```csharp
[OptionsValidator]
public partial class SmtpOptionsValidator : IValidateOptions<SmtpOptions>;

builder.Services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection("Smtp"))
    .ValidateOnStart();   // runs the generated validator registered above at startup
```

With the binding generator enabled and a generated validator registered, the options pipeline —
binding and validation — is reflection-free and AOT-safe. See `knowledge/csharp/platform.md` for
the options accessors.

### Regex {#regex}

```csharp
public partial class Validators
{
    [GeneratedRegex(@"^\d{3}-\d{2}-\d{4}$")]
    public static partial Regex Ssn();
}
```

Compile-time generated, no startup penalty, AOT-safe. Replace every `new Regex(constantPattern)`
with a `[GeneratedRegex]` partial method. Runtime `new Regex` is only for patterns not known at
compile time (e.g. user-supplied), and those should still be cached.

## Custom incremental generators {#custom}

Author an `IIncrementalGenerator` (the older `ISourceGenerator` is superseded — it re-runs on every
keystroke and does not cache). The pipeline shape:

```csharp
[Generator]
public sealed class MapperGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
                "MyApp.GenerateMapperAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => Extract(ctx))   // return a small value-equatable model
            .Where(static m => m is not null);

        context.RegisterSourceOutput(targets, static (spc, model) => Emit(spc, model!));
    }
}
```

Rules that keep the pipeline correct and fast:

- Use `ForAttributeWithMetadataName` for attribute-driven generators — it is dramatically cheaper
  than filtering all syntax nodes.
- The transform must return a small, value-equatable data model (record with value-type members or
  `EquatableArray<T>`). Passing `ISymbol`/`Compilation`/syntax nodes downstream breaks incremental
  caching and re-runs generation constantly.
- Keep predicates syntax-only and cheap; do semantic work in the transform.
- Emit deterministic output with `context.AddSource("Type.g.cs", source)`; stable hint names avoid
  churn.
- Report user errors as diagnostics (`context.ReportDiagnostic`) with a stable ID, location, and
  message — do not throw.

Marker-attribute pattern: define an attribute, require the target be `partial`, generate the other
half of the partial type. Ship the attribute in the generator assembly or a shared reference so
consumers can apply it.

Reference the generator as an analyzer, not a runtime dependency:

```xml
<ProjectReference Include="..\MyApp.Generators\MyApp.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Debugging generators {#debugging}

Emit generated files to disk to inspect them:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)/generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Files land under `obj/<config>/<tfm>/generated/<GeneratorAssembly>/<GeneratorType>/`. To step into
generator code, attach the debugger to the compiler (`csc`/`VBCSCompiler`) and rebuild, or add
`Debugger.Launch()` guarded behind a build property. If a generator seems not to run, confirm the
`OutputItemType="Analyzer"` reference and check `dotnet build -v:detailed` for the generator name.
Unit-test generators with the Roslyn testing packages — see
`knowledge/csharp/testing/source-generator-testing.md`.

## Migration from reflection {#migration}

1. Locate hotspots: reflective `JsonSerializer.Serialize`/`Deserialize` without a context,
   `new Regex(const)`, interpolated `ILogger` calls, reflective `Get<T>()` config binding.
2. Prioritize by startup and hot-path impact (measure with `dotnet-trace` / BenchmarkDotNet).
3. Introduce the generator form for new code first, then migrate module by module with tests green
   at each step.
4. Verify AOT/trim warnings clear (`<IsAotCompatible>true</IsAotCompatible>`), then remove the old
   reflective path.

## When reflection is acceptable {#when-reflection-is-acceptable}

Reflection stays legitimate for genuinely dynamic scenarios: plugin systems loading unknown
assemblies, developer tooling, or a third-party library that offers no generator hook. When it is
unavoidable on an AOT/trim target, mark the entry point with `[RequiresDynamicCode]` and
`[RequiresUnreferencedCode]` and document why no generator applies, so the trim analyzer and future
readers know the boundary is deliberate.
