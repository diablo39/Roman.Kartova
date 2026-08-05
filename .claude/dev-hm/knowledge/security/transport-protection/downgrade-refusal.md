# Transport protection — Downgrade refusal

Section of `knowledge/security/transport-protection.md`.


Failure to establish verified transport is a refused operation, never a retried one over a
weaker channel. Our clients treat verification failure as final:

- No fallback from `https` to `http` in client logic, configuration, or connection-string
  handling. Configuration carries no plaintext alternate URL "for resilience" — an endpoint list
  that mixes schemes is a downgrade waiting for a network hiccup.
- Retry and resilience wrappers (backoff, circuit breakers, failover lists) retry the same
  channel with the same verification policy. Whatever the retry policy varies, it never varies
  the transport downward.
- No re-negotiation below the floor on handshake failure: a peer that cannot complete a verified
  handshake at or above the floor is unreachable, and the error says so.
- Redirect handling refuses to follow a redirect from `https` to `http`. Server-side, our
  HTTP-serving code redirects plaintext to TLS and sets HSTS so browsers stop asking
  (header specifics in `knowledge/security/browser-protections.md`).

The verifying test binds a plaintext recorder next to an unverifiable TLS fixture and asserts
the recorder stays silent while the client raises: the failure stayed a failure. The same
recorder wraps resilience layers added around the client, so a later "make it more robust"
change cannot quietly add the plaintext path back.
