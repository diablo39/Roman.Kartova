# Deployment hardening — Image provenance and admission

Section of `knowledge/security/deployment-hardening.md`.


The cluster runs what we built, not what shares a name with it:

- Deployments reference images by digest; the tag is documentation, the digest is the claim.
- Images are signed in CI at build time; current practice binds the signature to the build's
  OIDC workload identity (keyless signing) rather than a long-lived key that becomes its own
  secret-management problem. Where a KMS-held key is used instead, it follows
  `knowledge/security/secrets-and-keys.md#key-lifecycle`.
- An admission policy verifies signatures — and provenance attestations where the build emits
  them (`knowledge/security/supply-chain.md#slsa`) — before scheduling, restricted to an
  allowlisted set of registries and signer identities. Rollout follows the same
  report-then-enforce path as CSP: audit mode first, then enforcement, and enforcement mode is
  itself configuration a policy test pins.
- The registry allowlist plus digest pinning closes the name-shadowing hole at deploy time the
  way registry pinning closes it at dependency time (SEC-061).
