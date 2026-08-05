# Security oracle — Transport and peer identity

Section of `oracles/security-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-150 | Peer identity on internal calls | New service-to-service calls authenticate the peer (mTLS, platform identity, signed tokens) or the handoff names the platform layer that does; zero new internal endpoints trusted by network position alone | S1 | A07 · CWE-306 · V12 | knowledge/security/transport-protection/peer-identity.md |
| SEC-151 | No downgrade fallback | Configuration and client code added by the diff contain no fallback from verified TLS to plaintext and no acceptance below the protocol floor on negotiation failure; failure to establish verified transport is a refused connection, and a control test asserts the refusal | S0 | A04 · CWE-757 · V12 | knowledge/security/transport-protection/downgrade-refusal.md |
| SEC-152 | PQC-compatible negotiation | TLS group configuration added by the diff does not pin classical-only key-exchange lists that exclude the platform's hybrid post-quantum defaults; n/a when negotiation is left to platform defaults | S3 | A04 · V12 | knowledge/security/cryptography-lifecycle/post-quantum.md |
