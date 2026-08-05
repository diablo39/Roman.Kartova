# Control-verification tests — Refusal assertions

Section of `knowledge/security/control-verification-tests.md`.


The protective behavior of a control is the refusal, so the test asserts the deny outcome —
not only that the allow path works. Each mapped control test asserts, observably:

| Control family | Refusal the test asserts |
|---|---|
| Authorization | a request carrying a role or identity without the permission receives the project's refusal shape (for example 403, or 404 where existence is hidden), and the protected operation had no effect |
| Authentication | a request with absent, expired, or unverifiable credentials is refused before the handler runs |
| Session | a pre-authentication or revoked session identifier no longer reaches an authenticated context |
| Transport | a peer our trust store cannot verify gets a refused connection with a verification error — and no request over a weaker channel |
| Data access | values with special characters round-trip as literal data; an identifier outside the allowlist is refused |
| Parsing | structured input outside the accepted contract is rejected before it reaches any sink |
| Encoding | markup characters in data render as text; no element or instruction is created from data |
| Telemetry | secret and regulated values do not appear in captured logs, traces, or error payloads |
| Resource limits | input beyond the configured size, depth, or count limit is refused with the limit error |

Assertion strength rules:

- Assert the specific refusal — the exact status code, exception type, or error code — never a
  loose "not success". A broadened assertion is a weakened control test.
- Where the refused operation would have had a side effect, assert its absence too: the record
  was not written, the message was not published, the endpoint received zero requests.
- Assert at the boundary where the control lives, so the test exercises the enforcement path
  the production request takes.
