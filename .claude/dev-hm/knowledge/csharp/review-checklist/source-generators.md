# C# review checklist — Source generators over reflection {#source-generators}

Section of `knowledge/csharp/review-checklist.md`.


| Check | Severity | Notes |
|---|---|---|
| JSON via `JsonSerializerContext` | S2 | No reflection-mode `JsonSerializer` on hot/AOT paths without a context. Oracle QUA-CS-002 |
| Logging via `LoggerMessage` on hot paths | S2 | No string-interpolated `ILogger` calls. Oracle QUA-CS-003 |
| Regex via `[GeneratedRegex]` | S2 | No `new Regex(constantPattern)`. Oracle QUA-CS-004 |
| Config via options binding | S2 | Typed options with `ValidateOnStart`; generator-friendly binding |
| Reflection justified when used | S2 | `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` on unavoidable reflection with a documented reason |

See `knowledge/csharp/source-generators.md` for the decision matrix and migration path.
