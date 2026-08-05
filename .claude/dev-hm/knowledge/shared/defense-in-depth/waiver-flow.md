# Waiver flow

1. Developer or reviewer marks the line `waive-requested` with a rationale.
2. The owning gate accepts (`waived`, rationale recorded in the gate report) or rejects (verdict
   stays `fail`). Ownership and limits per `knowledge/shared/severity-tiers.md`: S0 has no waiver
   path; SEC-* waivers only from senior-security-engineer; QUA-* only from senior-quality-engineer.
3. Waivers are per-ID, per-location, per-change. They expire with the change; nothing is
   grandfathered.
