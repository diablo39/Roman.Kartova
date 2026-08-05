# Security oracle — Memory safety (native code)

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-100 | Bounded buffer APIs | Zero unbounded C string/buffer functions (strcpy, strcat, sprintf, gets, scanf %s) in the diff; sized variants or safe abstractions used | S0 | CWE-120, CWE-121, CWE-122 · V15 | knowledge/security/secure-coding-review/memory-safety.md |
| SEC-101 | Sanitizer evidence | For native-code changes, tests were run under AddressSanitizer and UBSan and the clean run is recorded, or the run is reported "not run" with reason | S1 | CWE-787, CWE-125 | knowledge/security/secure-coding-review/memory-safety.md |
| SEC-102 | Size arithmetic checked | Allocation sizes and index arithmetic derived from external input are overflow-checked before allocation or indexing | S1 | CWE-190 | knowledge/security/secure-coding-review/memory-safety.md |
| SEC-103 | Lifetime discipline | No pointer or reference to freed or out-of-scope memory escapes its scope; heap ownership in C++ diffs is expressed through RAII/smart pointers rather than paired new/delete | S0 | CWE-416, CWE-476 | knowledge/security/secure-coding-review/memory-safety.md |
