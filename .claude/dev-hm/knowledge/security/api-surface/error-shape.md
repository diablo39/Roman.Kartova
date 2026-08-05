# API surface protection — Error shape

Section of `knowledge/security/api-surface.md`.


All error responses share one envelope — a machine-readable error code, a human message, and a
correlation id — and carry no stack traces, SQL fragments, filesystem paths, framework banners,
or dependency version strings (this is the response side of SEC-072 in
`knowledge/security/secure-coding-review.md`; detail belongs in server logs keyed by the
correlation id). Two shape decisions to make once and state in the contract:

- Existence disclosure: "not found" and "not yours" return the same status (conventionally 404)
  and an indistinguishable body, so the surface does not confirm which object ids exist.
- Validation errors name the offending field and constraint but never echo the submitted value —
  echoed values end up in logs, proxies, and screenshots, and the submitted value may be a
  credential pasted into the wrong box.

```json
{ "error": "invalid_request", "detail": "displayName exceeds maximum length", "correlationId": "01J..." }
```

Verification tests: force an unhandled internal failure (fault-injecting test double) and assert
the response matches the envelope schema and contains none of a marker list (`Exception`, `at `,
`SELECT`, path separators, framework names); request a foreign object and a nonexistent object
and assert the two responses are status- and shape-identical.
