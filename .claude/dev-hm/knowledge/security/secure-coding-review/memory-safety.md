# Secure code review by vulnerability class — Memory safety

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-100 – SEC-103 (native code; the CWE Top 25 2025 added buffer-overflow CWEs among
its six new entries). Reviewable without running anything: unbounded C string/buffer functions
(`strcpy`, `strcat`, `sprintf`, `gets`, `scanf %s`) fail SEC-100 on sight — require sized
variants or safe abstractions. Size arithmetic from external input is overflow-checked before
allocation or indexing (`n * size` can wrap; SEC-102). Ownership in C++ diffs is expressed
through RAII and smart pointers; raw `new`/`delete` pairs and pointers escaping their owner's
scope fail SEC-103. Runtime evidence: sanitizer runs (ASan + UBSan) recorded per SEC-101. Rust
`unsafe` blocks get the same review lens plus Miri.
