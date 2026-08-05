# Secure code review by vulnerability class — Resource consumption

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-120. Collection endpoints without pagination or a result cap, and request bodies
without a size limit, are single-request availability failures (CWE-770/400, OWASP API4). The
control may live in framework or gateway config — the handoff names which layer provides it.
