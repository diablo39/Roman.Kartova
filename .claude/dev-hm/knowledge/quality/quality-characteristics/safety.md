# Quality characteristics in review (ISO/IEC 25010:2023) — Safety

Section of `knowledge/quality/quality-characteristics.md`.


New in the 2023 revision; sub-characteristics include operational constraint, risk
identification, fail safe, hazard warning, safe integration. Relevant only where software
failure can harm people, property, or the environment (control systems, energy infrastructure,
medical, transport). Where it applies: fail safe converges with SEC-071's fail-closed rule —
the system moves to its defined safe state on error; operational constraints (limits, interlocks)
are enforced in code and tested at their boundaries (QUA-004); and hazard-relevant changes
escalate to the domain's safety process — a code-review gate does not certify functional safety
(IEC 61508-class work), it only refuses changes that visibly weaken documented constraints.
