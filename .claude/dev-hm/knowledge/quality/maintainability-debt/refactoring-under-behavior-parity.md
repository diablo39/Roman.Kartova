# Maintainability and technical-debt control — Refactoring under behavior parity

Section of `knowledge/quality/maintainability-debt.md`.


Every control above eventually lands here: paying debt means changing structure without changing
behavior, and QUA-006 is the contract — a refactor modifies no test assertions and no public
contracts, and the pre-existing suite passes unmodified.

- Characterization tests come first when touching untested legacy code: pin the current
  behavior, including its oddities, then move. The oddity you silently fix is the behavior
  somebody depends on.
- Steps stay releasable. A series of small parity-preserving diffs beats one big bang; each step
  leaves the build green and shippable, so the work can pause without stranding a branch.
- Replace large modules with the strangler pattern: the new implementation grows beside the old
  behind the same interface, callers move over incrementally, and the old one is deleted. The
  deletion is the point — a strangler that never finishes is two systems.
- Behavior changes discovered mid-refactor become their own change with their own tests. Fixing
  a bug inside a "pure refactor" diff makes both the fix and the refactor unreviewable.
