# Browser protections — CSP

Section of `knowledge/security/browser-protections.md`.


Every HTML-serving application sets a Content-Security-Policy built on per-response nonces or
hashes, not on host allowlists. Host lists erode: each newly trusted host widens what may
execute, and the policy's meaning drifts away from review. The baseline:

```
Content-Security-Policy:
  default-src 'self';
  script-src 'nonce-{random}' 'strict-dynamic';
  object-src 'none';
  base-uri 'none';
  form-action 'self';
  frame-ancestors 'none'
```

`script-src` contains neither `'unsafe-inline'` nor a wildcard host — the two entries that make
the rest of the policy decorative. `'strict-dynamic'` lets a nonced script load its own
dependencies, which is what makes nonce-based policies workable with bundlers; the nonce is
generated per response, never a build-time constant. `object-src 'none'` and `base-uri 'none'`
close the legacy execution and base-rewrite paths. Roll out through
`Content-Security-Policy-Report-Only` with a reporting endpoint, fix what reports, then
enforce — and keep reporting enabled after enforcement, because reports are how policy
regressions surface. The header is set in one place (middleware or gateway); the handoff names
which layer owns it.

Verification tests: the policy-shape, per-response-nonce, and refused-execution patterns in
`knowledge/security/control-test-patterns-browser.md#csp-policy-shape-and-refused-execution`.
