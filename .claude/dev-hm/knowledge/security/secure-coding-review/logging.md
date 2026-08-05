# Secure code review by vulnerability class — Logging

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-080 – SEC-082. Security-relevant events added by the diff — authentication
attempts, authorization denials, validation rejections at trust boundaries — log actor, action,
outcome, and timestamp through the project logger (SEC-080); OWASP Top 10:2025 A09 renames the
category to Security Logging and Alerting Failures because a log nobody can alert on fails its
purpose. External input written into log messages goes through structured fields, or has CR/LF
and control characters stripped, so forged lines cannot be injected (SEC-081). No PII, tokens,
or payment data in logs; identifiers appear masked or truncated (SEC-082) — the common leak is
logging a whole request object that contains an Authorization header.
