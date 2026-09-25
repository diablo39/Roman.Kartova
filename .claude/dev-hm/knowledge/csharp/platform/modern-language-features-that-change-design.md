# Modern .NET platform patterns — Modern language features that change design

Section of `knowledge/csharp/platform.md`.


| Feature | Use it for |
|---|---|
| `record` / `record struct` | Immutable DTOs and value objects; structural equality; `with` expressions |
| `required` members + primary constructors | Enforce initialization without boilerplate constructors |
| Pattern matching / `switch` expressions | Exhaustive branching over shapes and enums; replaces type-check ladders |
| Collection expressions `[...]` and spreads | Concise, allocation-aware collection init |
| `field` keyword (field-backed properties) | Custom accessor logic without a hand-declared backing field |
| Extension members | Add methods/properties to types you do not own, including static and interface members |
| Nullable reference types | Encode nullability in the type system; annotate APIs precisely |

Nullability discipline: annotate public APIs precisely, avoid the null-forgiving `!` operator
except where an invariant is provably established, and do not silence the compiler with
`#nullable disable`.
