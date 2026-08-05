# C# review checklist — Nullability {#nullability}

Section of `knowledge/csharp/review-checklist.md`.


Enable NRT solution-wide. Annotate public APIs precisely (`?` where null is valid). Reserve the
null-forgiving `!` for cases where an invariant is established that the compiler cannot see, and
comment why. A diff that adds `#nullable disable` or blanket-`!`s away warnings is masking a design
gap — treat as S1.
