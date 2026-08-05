# Transport protection — Peer identity

Section of `knowledge/security/transport-protection.md`.


Server identity verification protects the outbound half; service-to-service calls also need the
inbound half — our services verify who is calling before trusting the call. Network position is
not identity: being reachable on the cluster network, a VPC, or a VPN proves routing, not
authorization (NIST SP 800-207). Every internal endpoint authenticates its peers by one of:

| Mechanism | Shape | Where it fits |
|---|---|---|
| Mutual TLS | Both sides present certificates; identity is the verified certificate identity (for example a SPIFFE ID issued by the mesh) | Platforms with a service mesh or workload certificate issuance; the mesh states in `knowledge/shared/versions.md` pins |
| Platform workload identity | The caller presents a platform-issued, short-lived token (managed identity, service-account token) verified against the platform issuer | Cloud and Kubernetes workloads; pairs with the workload-identity table in `knowledge/security/secrets-and-keys.md` |
| Signed service tokens | The caller mints a signed token from its service credential; the callee verifies signature, issuer, audience, and expiry | Environments without mesh or platform identity |

Rules that keep the control meaningful:

- When the platform layer (mesh sidecar, gateway) performs the verification, the handoff names
  that layer, and our code still authorizes the verified identity — authentication of the peer
  and authorization of the operation are separate checks
  (`knowledge/security/authorization-design.md`).
- The callee verifies; the caller's good behavior is not the control. An internal endpoint that
  accepts anonymous calls because "only our services can reach it" has no peer identity control.
- Identities are per-workload, not shared: one credential per service, so revocation and audit
  have a subject.

The control test drives a request without peer credentials (no client certificate, no workload
token) at an internal endpoint through the real middleware and asserts refusal before handler
logic runs — the same shape as the unauthenticated-request pattern in
`knowledge/security/control-test-patterns-access.md`.
