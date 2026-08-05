# Maintainability and technical-debt control — Sub-characteristics as controls

Section of `knowledge/quality/maintainability-debt.md`.


| Sub-characteristic | The control we build | Observable signal that it holds |
|---|---|---|
| Modularity | Modules depend in one documented direction and are replaceable behind interfaces | Import graph stays acyclic (QUA-026); boundary rules hold (QUA-020); a module's fan-out stays bounded — one importing twenty others is a coordination point to split |
| Reusability | Shared logic exists once, behind a name | No duplicated block of ten or more lines (QUA-021); the clone detector's duplicate count does not rise |
| Analysability | A newcomer traces behavior without a debugger | Functions within the length and complexity limits (QUA-022); no dead code (QUA-023); the touched-code ratchet below |
| Modifiability | One behavior change lands in one place | A single-concern change edits one module plus its tests; repeated literals are named constants (QUA-024); shotgun edits across many files signal misplaced responsibility |
| Testability | Seams exist without patching internals | New units are constructible with injected dependencies; their tests need no monkey-patching, reflection into privates, or global-state setup |

The signals are trend instruments, not per-diff gates: a single diff rarely fails them, and a
quarter of diffs quietly can. Watch the direction, not the snapshot.
