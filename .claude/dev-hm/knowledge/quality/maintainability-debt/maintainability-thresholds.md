# Maintainability and technical-debt control — Maintainability thresholds

Section of `knowledge/quality/maintainability-debt.md`.


The per-diff limits (duplication, function size and complexity, dead code, named constants,
cycles) are defined once in `knowledge/quality/quality-characteristics.md#maintainability-thresholds`
and are not restated here. Over time, the thresholds that matter are ratchets — values allowed to
improve or hold, never to quietly worsen:

- Touched code gets no worse. A file the diff modifies does not end with higher cyclomatic
  complexity or length than it started unless the handoff says why. Improving is welcome;
  degrading is a decision, and decisions are recorded.
- Justified exceptions trend flat or down. The escape-hatch comments permitted by the per-diff
  thresholds are cheap one at a time; their count per module is the honest measure of drift. A
  rising count is a refactoring trigger, not a style complaint.
- Duplication density does not rise where the repository runs a clone detector (jscpd, PMD CPD,
  or the linter's own); the count is cheap to record in CI.
- Deprecation warnings do not rise (see deprecation hygiene below).
- New public surface is intentional: every new exported symbol is either used outside its module
  or documented as API. Accidental exports become compatibility promises nobody meant to make.

A ratchet needs a baseline. Where a tool reports the number, record it once and compare in CI;
where none does, the reviewer compares the touched files before and after — which is exactly the
scope a diff review already has.
