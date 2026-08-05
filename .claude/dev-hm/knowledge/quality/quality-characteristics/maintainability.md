# Quality characteristics in review (ISO/IEC 25010:2023) — Maintainability

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics: modularity, reusability, analysability, modifiability, testability. This is
the characteristic most review time goes to, and the one with the most deterministic thresholds
(below). Beyond the thresholds, review for: names that say what things are for; one level of
abstraction per function; dependencies pointing in the documented direction (QUA-020); code that
a newcomer can trace without a debugger (analysability); seams that let behavior be tested
without patching internals (testability — a design property, visible in the diff as constructor
injection and boundary interfaces). Analysability is also enforced on the operational side —
project logger (QUA-040), metrics/tracing conventions and context propagation (QUA-042 –
QUA-045) — and modifiability on the dependency side: additions justified (QUA-060), generated
files never hand-edited (QUA-063), feature flags with a stated cleanup path (QUA-102). Debt
accumulation across diffs — registration, paydown, deprecation hygiene, dependency currency —
lives in `knowledge/quality/maintainability-debt.md`.
