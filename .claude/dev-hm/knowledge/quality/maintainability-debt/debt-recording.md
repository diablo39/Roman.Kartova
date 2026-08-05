# Maintainability and technical-debt control — Debt recording

Section of `knowledge/quality/maintainability-debt.md`.


What counts as debt: any known shortcoming that will cost future work. Skipped refactors,
threshold exceptions that outlive their excuse, workarounds for upstream bugs, missing tests on
legacy code the diff had to touch, boundary freezes from the section above, configuration that
only one person understands.

- A deliberate shortcut ships with its debt entry in the same change. The entry is the
  difference between a decision and an accident.
- One register, agreed once: the issue tracker under a debt label, or a debt file in the repo.
  Debt scattered across heads, chats, and comments is not a register.
- Entry format, one line each: where (path), what and why, cost when unpaid (what gets slower or
  riskier), remediation sketch, owner and date. Five lines; if it takes a page, it is a design
  document, not a debt entry.
- Code-level markers point at the register: a TODO or FIXME carries the tracker reference. A
  bare TODO is a note to nobody and is dead code's cousin (QUA-023 adjacent).
