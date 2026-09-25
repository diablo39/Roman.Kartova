# Modern .NET platform patterns — Options pattern (`IOptions` family)

Section of `knowledge/csharp/platform.md`.


Bind configuration sections to typed classes; validate at startup so misconfiguration fails the
deployment, not the first request.

```csharp
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

| Accessor | Lifetime / semantics | Use for |
|---|---|---|
| `IOptions<T>` | Singleton, computed once | Static config that never changes at runtime |
| `IOptionsSnapshot<T>` | Scoped, recomputed per request | Config that may change between requests |
| `IOptionsMonitor<T>` | Singleton with change notifications | Singletons that must react to reloads (`OnChange`) |

The options-validation source generator can replace the reflective `ValidateDataAnnotations()`
call, but it is opt-in: declare an `[OptionsValidator]` partial class implementing
`IValidateOptions<T>` and register it in DI — only then may `ValidateDataAnnotations()` be
removed (removing it without that registration silently disables validation). The config-binding
generator makes `Bind`/`Get` reflection-free. See
`knowledge/csharp/source-generators.md#configuration`.
