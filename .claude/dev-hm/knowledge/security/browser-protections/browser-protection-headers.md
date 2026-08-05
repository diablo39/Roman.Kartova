# Browser protections — Browser protection headers

Section of `knowledge/security/browser-protections.md`.


The header set every HTML response carries, alongside the CSP:

| Header | Value | Control |
|---|---|---|
| `Content-Security-Policy: frame-ancestors` | `'none'`, or the exact allowed embedder | Framing control (CWE-1021); supersedes `X-Frame-Options`, which remains only as a legacy fallback |
| `Strict-Transport-Security` | long `max-age` with `includeSubDomains` | Browser refuses plaintext for the whole host; preload is a deliberate, hard-to-reverse decision made once |
| `X-Content-Type-Options` | `nosniff` | Responses are interpreted only as their declared type |
| `Referrer-Policy` | `strict-origin-when-cross-origin` or stricter | URLs with identifiers do not leak cross-origin |
| `Cross-Origin-Opener-Policy` | `same-origin` | Our windows are not scriptable by pages that opened them |
| `Cross-Origin-Resource-Policy` | `same-origin` / `same-site` | Responses not meant for embedding refuse cross-origin inclusion |

Embedding is deny-by-default: an application that must be framed by a known partner allows that
one origin in `frame-ancestors` and records it in the handoff. As with the CSP, one middleware
or gateway layer owns the set.

Verification tests: the route-class header snapshot — error responses included — in
`knowledge/security/control-test-patterns-browser.md#protective-header-set-snapshot-per-route-class`.
