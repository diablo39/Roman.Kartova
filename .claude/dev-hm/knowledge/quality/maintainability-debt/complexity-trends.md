# Maintainability and technical-debt control — Complexity trends

Section of `knowledge/quality/maintainability-debt.md`.


The hotspot method finds where maintainability debt charges interest: cross change frequency
with complexity. Files that are both churned often and complex are where the next defect and the
next slow estimate live; complex-but-frozen files can wait.

- Compute it cheaply: change counts per file from the version-control log over the last six to
  twelve months, times the linter's complexity figure per file. A spreadsheet suffices; tools
  that do this natively exist, but the signal does not require them.
- A hotspot that holds its place across two consecutive looks gets a refactoring task with a
  stated, measurable target — "the top function's complexity halved", "the file split along the
  two responsibilities it names" — not a vague cleanup wish.
- Read the exception ratchet and the hotspot list together: a module accumulating justified
  exceptions that is also a hotspot is the strongest paydown candidate the numbers can nominate.
