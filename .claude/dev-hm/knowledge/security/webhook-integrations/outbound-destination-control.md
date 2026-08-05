# Webhook integrations — Outbound: destination control

Section of `knowledge/security/webhook-integrations.md`.


The destination URL is customer configuration — external input that our server will connect to,
which is the SSRF shape (SEC-090):

- Validate on registration and again at send time: `https` scheme only, parsed-host comparison
  (SEC-091), resolved address outside link-local, metadata, and private ranges unless the
  deployment explicitly serves internal consumers — then an allowlist, not an exemption.
- Redirects are not followed; a redirecting endpoint is a failed delivery, because a redirect
  re-opens the destination question after validation.
- TLS verification stays on (SEC-031) — a consumer with a broken certificate is a failed
  delivery, never a `verify=false` special case.
- Deliveries run from an egress-restricted worker where the platform offers it
  (`knowledge/security/deployment-hardening.md#identity-and-network-least-privilege`), so the
  network enforces what the code validates.
