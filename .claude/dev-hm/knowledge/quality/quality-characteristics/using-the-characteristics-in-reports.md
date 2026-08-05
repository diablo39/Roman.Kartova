# Quality characteristics in review (ISO/IEC 25010:2023) — Using the characteristics in reports

Section of `knowledge/quality/quality-characteristics.md`.


Tag findings with the characteristic they degrade ("maintainability (analysability): 120-line
function...") — it makes gate reports auditable against a stable external model instead of
reviewer taste, and it exposes coverage gaps (a report with only maintainability findings on a
change full of new external calls has probably under-reviewed reliability). The mapping table
at `oracles/quality-oracle.md` (coverage map) is the authoritative index from characteristic to
oracle IDs.
