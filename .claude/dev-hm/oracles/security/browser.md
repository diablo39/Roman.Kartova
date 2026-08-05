# Security oracle — Browser client protections

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-170 | CSP baseline | New HTML-serving applications set a Content-Security-Policy whose script-src contains neither 'unsafe-inline' nor a wildcard host, or the handoff names the platform layer that sets it | S2 | A02 · CWE-693 · V3 | knowledge/security/browser-protections/csp.md |
| SEC-171 | Browser token storage | Session-equivalent tokens persisted by new browser code use HttpOnly cookies where the flow permits; storage in localStorage/sessionStorage is a declared decision with the mitigating controls named | S2 | A07 · CWE-522 · V7 | knowledge/security/browser-protections/token-storage.md |
| SEC-185 | Response cache hygiene | Responses added by the diff that carry authenticated content or Tier-3 data set `Cache-Control: no-store` (or the project's stated equivalent no-caching directive), in the handler or in the platform layer the handoff names; zero such responses without a cache directive | S2 | CWE-525 · V3 | knowledge/security/browser-protections/response-cache-hygiene.md |
