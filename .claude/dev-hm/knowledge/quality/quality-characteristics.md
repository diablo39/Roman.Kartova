# Quality characteristics in review (ISO/IEC 25010:2023)

How the nine product quality characteristics of ISO/IEC 25010:2023 translate into review
questions and oracle checks. The 2023 revision replaced usability with interaction capability,
replaced portability with flexibility, added safety as a ninth characteristic, and moved
quality-in-use to ISO/IEC 25019:2023. Use the characteristic names as the shared vocabulary in
findings and gate reports; the deterministic subset is enforced by `oracles/quality-oracle.md`,
and the rest is reviewed as `finding` lines with severities from
`knowledge/shared/severity-tiers.md`.

| Characteristic | Core question for a diff | Deterministic enforcement | Depth file |
|---|---|---|---|
| Functional suitability | Does it do the required thing, correctly, and nothing surprising? | QUA-001 – QUA-006, QUA-034, QUA-093 – QUA-094 | `knowledge/quality/data-migration-safety.md` (data moves) |
| Performance efficiency | Does it meet time/resource expectations under stated load? | QUA-070 – QUA-074 | `knowledge/quality/performance-capacity.md` |
| Compatibility | Does it coexist and interoperate without breaking neighbors? | QUA-017, QUA-034, QUA-090, QUA-113; stack addenda | `knowledge/quality/api-compatibility.md`; `knowledge/quality/test-adequacy/contract-tests.md` |
| Interaction capability | Can users operate it effectively (incl. accessibility)? | QUA-110 – QUA-111 (automated subset); rest as findings | `knowledge/quality/accessibility.md` |
| Reliability | Does it stay correct under faults and over time? | QUA-030 – QUA-033, QUA-041, QUA-080 – QUA-082, QUA-091, QUA-092, QUA-101, QUA-112 | `knowledge/quality/reliability-resilience.md`; recoverability: `knowledge/quality/data-migration-safety.md`, `knowledge/quality/release-readiness.md` |
| Security | Confidentiality, integrity, accountability preserved? | The SEC-* oracle | `knowledge/security/` family files |
| Maintainability | Can the next person change it safely and cheaply? | QUA-020 – QUA-027, QUA-040, QUA-042 – QUA-045, QUA-050 – QUA-053, QUA-060, QUA-063, QUA-102 | `knowledge/quality/maintainability-debt.md` |
| Flexibility | Can it adapt to new environments, scale, be installed/replaced? | QUA-026, QUA-051; mostly design review | — |
| Safety | Can it contribute to harm to people or property? | Domain-specific; see below | — |

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Functional suitability | `knowledge/quality/quality-characteristics/functional-suitability.md` |
| Performance efficiency | `knowledge/quality/quality-characteristics/performance-efficiency.md` |
| Compatibility | `knowledge/quality/quality-characteristics/compatibility.md` |
| Interaction capability | `knowledge/quality/quality-characteristics/interaction-capability.md` |
| Reliability | `knowledge/quality/quality-characteristics/reliability.md` |
| Security | `knowledge/quality/quality-characteristics/security.md` |
| Maintainability | `knowledge/quality/quality-characteristics/maintainability.md` |
| Maintainability thresholds | `knowledge/quality/quality-characteristics/maintainability-thresholds.md` |
| Flexibility | `knowledge/quality/quality-characteristics/flexibility.md` |
| Safety | `knowledge/quality/quality-characteristics/safety.md` |
| Using the characteristics in reports | `knowledge/quality/quality-characteristics/using-the-characteristics-in-reports.md` |
