# Secure code review by vulnerability class — Browser protection headers

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-048. HTML-serving responses need a framing control — CSP `frame-ancestors` or
`X-Frame-Options: DENY` — against clickjacking (CWE-1021), and HTTPS services set HSTS. These
normally live in one middleware or gateway config; the diff sets them or names that layer.
