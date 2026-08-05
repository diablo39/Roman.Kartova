# Security oracle — Cross-origin and request forgery

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-046 | CSRF protection | New state-changing endpoints authenticated by cookies carry an anti-CSRF control: the framework's CSRF token mechanism, or SameSite=Lax/Strict cookies combined with rejecting state changes on safe methods | S1 | A01 · CWE-352 · V3 | knowledge/security/secure-coding-review/cross-origin-and-request-forgery.md |
| SEC-047 | CORS restraint | Added CORS configuration does not combine credentials with a wildcard or request-reflected origin; allowed origins are an explicit list | S1 | A02 · CWE-942 · V3 | knowledge/security/secure-coding-review/cross-origin-and-request-forgery.md |
| SEC-048 | Browser protection headers | Responses serving HTML set a framing control (CSP frame-ancestors or X-Frame-Options), and HTTPS services set HSTS — in the diff or in the platform layer the handoff names | S2 | A02 · CWE-1021 · V3 | knowledge/security/secure-coding-review/browser-protection-headers.md |
