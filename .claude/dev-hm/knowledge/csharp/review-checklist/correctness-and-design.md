# C# review checklist — Correctness and design

Section of `knowledge/csharp/review-checklist.md`.


| Check | Severity | Notes |
|---|---|---|
| Nullable reference types enabled and honored | S1 | `<Nullable>enable</Nullable>`; no `#nullable disable` added; `!` only where an invariant is proven. Oracle QUA-CS-001. See {#nullability} |
| Error handling meaningful | S1 | No empty `catch`; no `catch (Exception)` that swallows; rethrow with `throw;` not `throw ex;`. See {#error-handling} |
| Exceptions for exceptional flow only | S2 | No control flow via exceptions on hot paths; validate and return results instead |
| Correct equality/hashing | S2 | Value types and value objects override `Equals`/`GetHashCode` consistently, or are `record`s |
| No captured `struct` boxing / unintended allocation on hot paths | S3 | Watch LINQ in tight loops, closures, boxing of value types |
| Public API surface intentional | S2 | Types/members `internal` unless part of the contract; `sealed` by default for non-inheritable types |
| Immutability where practical | S3 | `record`, `readonly`, `init`-only for DTOs and value objects |
