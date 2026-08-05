# Security oracle — Deserialization

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-050 | No native deserialization of untrusted data | Zero language-native object deserialization (pickle, ObjectInputStream, BinaryFormatter, unsafe YAML load) on external input; data crosses boundaries as schema-validated data-only formats | S0 | A08 · CWE-502 · V15 | knowledge/security/secure-coding-review/deserialization-and-parsers.md |
| SEC-051 | XXE disabled | XML parsers that touch external input are configured with DTDs and external entity resolution disabled | S1 | A02 · CWE-611 · V1 | knowledge/security/secure-coding-review/deserialization-and-parsers.md |
| SEC-052 | Parser resource limits | Parsers of external data enforce size, depth, and entity limits; archive extraction caps decompressed size and entry count | S2 | CWE-770 · V2 · API4 | knowledge/security/secure-coding-review/deserialization-and-parsers.md |
