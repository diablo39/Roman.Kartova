# Mutation testing evidence — sorting + pagination slice (ADR-0095)

**Date:** 2026-05-05
**Tool:** Stryker.NET 4.14.1
**Mode:** incremental (`--since:master`)
**Projects mutated:** 13 (per `mutation-targets.json`)
**Final mutation score:** **98.92%** (target ≥ 80%)
**Status:** PASS

## Loop convergence

| Pass | Score | Survivors | Action taken |
|---|---|---|---|
| 1 (baseline) | 89.25% | 10 | mutation-sentinel run on branch diff |
| 2 (after first test-generator pass) | 97.85% | 2 | 9 new tests added to kill survivors in `CursorCodec`, `SortSpec`, `QueryablePagingExtensions`, `Application` |
| 3 (after final tiebreaker test) | 98.92% | 1 | desc-tiebreaker test killed the last cursor-keyset survivor; remaining mutant accepted as near-equivalent |

## Surviving mutant — accepted as near-equivalent

**File:** `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs`
**Mutator:** Conditional (true) on `ParameterReplaceVisitor.VisitParameter`
**Original:** `node == _from ? _to : base.VisitParameter(node)`
**Mutated:** `(true ? _to : base.VisitParameter(node))` — i.e., always returns `_to`.

**Why accepted:** `VisitParameter` is only invoked by `ExpressionVisitor.Visit` on `ParameterExpression` nodes. In our single-parameter expression-tree replacement (used by `ApplyKeysetFilter`), every `ParameterExpression` encountered IS `_from` — there's no other parameter in scope. The `else` branch is functionally unreachable for all real call sites; the mutation produces identical observable behavior.

The acceptance is documented inline in the source via a comment near the visitor class, citing this evidence file.

## Tests added in this loop

| Test method | Mutant killed | File |
|---|---|---|
| `LimitAtMaxBoundary_does_not_throw` | Equality `:50` | `QueryablePagingExtensionsTests.cs` |
| `PagingForward_with_desc_order_yields_no_duplicates_no_skips` | Conditional `:140` | `QueryablePagingExtensionsTests.cs` |
| `PagingForward_with_desc_order_string_sort_yields_correct_order` | Conditional `:133` | `QueryablePagingExtensionsTests.cs` |
| `TieOnSortValue_with_desc_uses_id_as_descending_tiebreaker` | Conditional `:145` | `QueryablePagingExtensionsTests.cs` |
| `Encode_produces_compact_output_without_whitespace` | Boolean `:20` | `CursorCodecTests.cs` |
| `Encode_then_Decode_roundtrips_true_bool_sort_value` | Boolean `:76` | `CursorCodecTests.cs` |
| `Encode_then_Decode_roundtrips_false_bool_sort_value` | Boolean `:77` | `CursorCodecTests.cs` |
| `CompiledKeySelector_caches_the_delegate_across_accesses` | CoalesceAssignment `:24` | `SortSpecTests.cs` |
| `Create_throws_ArgumentException_with_empty_message_for_blank_name` | Statement `:87` | `ApplicationTests.cs` |

## Score computation

```
Total mutants emitted by Stryker:    738
Compile errors (excluded):          -105
Ignored / non-valid (excluded):     -540
                                   ----
Valid mutants in denominator:         93
Killed (incl. timeouts):              92
Survived:                              1 (accepted as near-equivalent)
NoCoverage:                            0

Score = 92 / 93 = 98.92%
```

## DoD bullet 7 satisfied

Per `CLAUDE.md` Definition of Done bullet 7:

> Mutation feedback loop run on changed files: `mutation-sentinel` (find surviving mutants) → `test-generator` (strengthen tests until mutants are killed). Mutation score must meet the repo target (≥80% per `stryker-config.json`). Document the score and any surviving mutants accepted as low-value.

- ✅ Loop run end-to-end (3 iterations).
- ✅ Score 98.92% > target 80%.
- ✅ Surviving mutant documented above with acceptance reason.
