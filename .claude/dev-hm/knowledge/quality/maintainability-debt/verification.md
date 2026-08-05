# Maintainability and technical-debt control — Verification

Section of `knowledge/quality/maintainability-debt.md`.


What the gate can decide deterministically: the cycle check is clean (tool run recorded, or the
added imports traced); measured counts — duplication, deprecation warnings, lint exceptions — did
not rise where the repository measures them; refactor diffs ran the unmodified suite green. What
stays review judgment: whether a shortcut the diff makes visible has its debt entry, whether a
paydown claim points at a merged diff, and whether a justified exception's justification still
holds. Findings are tagged with the sub-characteristic they degrade, per
`knowledge/quality/quality-characteristics.md#using-the-characteristics-in-reports`.
