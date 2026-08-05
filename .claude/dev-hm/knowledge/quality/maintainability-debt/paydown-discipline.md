# Maintainability and technical-debt control — Paydown discipline

Section of `knowledge/quality/maintainability-debt.md`.


Recording without repayment is a diary, not a control.

- Keep a standing budget: a fixed share of each iteration goes to repayment — ten to twenty
  percent is common; the exact number matters less than being nonzero and defended when the
  schedule tightens.
- Prioritize by interest, not size. Debt in a file changed weekly costs more than debt in a file
  untouched for a year; the hotspot method below finds where interest is highest.
- Debt implicated in an incident or in a visibly slowed feature is repaid in the next iteration,
  not renegotiated.
- Repayment diffs are refactors under QUA-006 parity: behavior-preserving, no test assertions
  modified, pre-existing suite green unmodified. Keeping paydown separate from feature work is
  what makes both reviewable.
- Closing an entry means the code changed. Entries do not age out; an entry nobody intends to
  pay is either deleted as a recorded accepted risk or kept honest.
