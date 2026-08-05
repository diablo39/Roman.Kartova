# Security oracle — Data classification

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


Verdicts in this section anchor to the declared tier; suspected under-classification is reported
as a `finding` with the reason, never as a fail (disputed classifications are adjudicated at the
gate, `knowledge/security/data-classification/tiers.md`).

| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-140 | Tier declared | Every new persisted field, message schema, or store carrying personal, payment, health, credential, or otherwise confidential-regulated data declares its classification tier (schema annotation, data-catalog entry, or handoff line); zero undeclared Tier-3 fields | S1 | V14 | knowledge/security/data-classification/tiers.md |
| SEC-141 | Tier-3 encrypted at rest | New Tier-3 data at rest is covered by a named encryption mechanism (storage-level, column/field-level, or application-layer envelope encryption); the mechanism is cited in the handoff | S1 | A04 · CWE-311 · V14 | knowledge/security/data-classification/encryption.md |
| SEC-142 | Regulated data out of telemetry | Zero Tier-3 values written by the diff into logs, traces, metric labels, analytics events, error payloads, or crash reports; identifiers appear masked or tokenized (extends SEC-082 beyond logs) | S1 | A09 · CWE-532 · V14 | knowledge/security/data-classification/telemetry.md |
| SEC-143 | Minimization | Each newly collected Tier-3 datum has a recorded purpose in the handoff; zero fields collected without one | S2 | V14 | knowledge/security/data-classification/minimization.md |
| SEC-144 | Retention and deletion path | New Tier-3 stores state a retention period and reference the deletion mechanism (TTL, purge job, cascade) that covers them | S2 | V14 | knowledge/security/data-classification/retention.md |
| SEC-145 | Masked display and export | Tier-3 identifiers rendered in UIs, reports, or exports added by the diff are masked or truncated, unless the handoff declares the surface requires the full value | S2 | CWE-359 · V14 | knowledge/security/data-classification/masking.md |
| SEC-146 | Residency stated | New storage or processing locations for Tier-3 data state the region and match the project's declared residency constraint; n/a when the project declares none | S2 | V14 | knowledge/security/data-classification/residency.md |
