# Security oracle — Concurrency

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-110 | Consistent guarding | Every shared mutable field or object the diff touches is accessed under one consistent discipline (same lock, atomics, or channel/actor confinement); zero mixed guarded/unguarded access to the same data | S1 | CWE-362 | knowledge/security/secure-coding-review/concurrency.md |
| SEC-111 | Atomic check-then-act | Check-then-act sequences on shared resources are atomic: exclusive-create file APIs instead of exists-then-open, compare-and-set instead of read-then-write | S1 | CWE-367 | knowledge/security/secure-coding-review/concurrency.md |
| SEC-112 | Lock ordering | When the diff acquires more than one lock, the acquisition order is documented and identical across call sites | S2 | CWE-833 | knowledge/security/secure-coding-review/concurrency.md |
