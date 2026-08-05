# Maintainability and technical-debt control — Dependency cycles

Section of `knowledge/quality/maintainability-debt.md`.


A cycle between modules removes the option to understand, test, or replace either side alone,
and it never breaks itself — every later change has one more reason to add one more edge. The
control is "no new cycles" (QUA-026), enforced at the first edge, because the first edge is the
cheap one.

Detection, per stack, wired as a CI check that fails on the first new edge where a tool exists:

| Stack | Tool | Note |
|---|---|---|
| TypeScript/JavaScript | dependency-cruiser or madge | Rules as config in the repo; `no-circular` on by default |
| Python | import-linter | Declare layer and independence contracts; runs as a test |
| Java | ArchUnit | Cycle and layer rules written as unit tests |
| C# | ArchUnitNET or NetArchTest | Same pattern: architecture rules as tests |
| Rust | compiler + cargo-modules | Crate-level cycles are impossible; watch module tangles inside a crate |
| C++ | include-what-you-use; CMake target graph | The target dependency graph must stay a DAG |
| Flutter/Dart | analyzer plus configured import lints | Keep feature packages one-directional |

Where no tool exists, trace the imports the diff adds by hand — the QUA-026 pass criterion is
written to allow exactly that.

Breaking a cycle, in order of preference: invert the offending edge (define the interface where
it is consumed, implement it where it was called); extract the shared piece into a third module
both sides import; replace the back edge with an event or callback so the lower layer stops
naming the upper one. Legacy tangles that are too expensive to break now get a boundary freeze —
no new edges into or out of the tangle — plus a debt entry, so the freeze is a recorded loan and
not a permanent state.
