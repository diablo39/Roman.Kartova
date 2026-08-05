# Cryptography lifecycle — Post-quantum

Section of `knowledge/security/cryptography-lifecycle.md`.


Recorded ciphertext can be stored today and decrypted when a cryptographically relevant quantum
computer exists, so the confidentiality lifetime of the data — not the arrival date of the
machine — sets the deadline. Standards are settled: ML-KEM (FIPS 203) for key encapsulation,
ML-DSA (FIPS 204) and SLH-DSA (FIPS 205) for signatures; deployment state for hybrid TLS key
exchange and PQC certificates is pinned in `knowledge/shared/versions.md`.

What our code does, in priority order:

- Key exchange first, via the platform: hybrid classical-plus-ML-KEM TLS key exchange is the
  deployed mainstream. Leave TLS group negotiation to platform defaults, and do not pin
  classical-only named-group lists in client or server configuration — a pinned classical list
  is the silent downgrade of the post-quantum transition. Where configuration must enumerate
  groups, it includes the platform's hybrid defaults.
- Long-lived confidentiality: data whose secrecy must outlast the transition gets envelope
  encryption now, so the key-wrapping path can move to PQC without re-processing the data.
- Signatures on their own clock: forgery requires the quantum computer at verification time, so
  signatures are less urgent than key exchange — but formats stay algorithm-agile (see above) so
  ML-DSA slots in when the certificate and library chain supports it, and long-lived signatures
  (releases, archival records) migrate first.
- Room for bigger primitives: PQC keys, ciphertexts, and signatures are larger than their
  classical counterparts. Schemas, columns, and protocol fields that store them declare sizes
  that accommodate the PQC variants, so the migration is a configuration change rather than a
  data-model change.

Verification: a configuration assertion that our TLS setup excludes no hybrid group and pins no
classical-only list; where the platform exposes it, an integration handshake against a
hybrid-preferring peer asserts a hybrid group was negotiated. NIST's 2030/2035 roadmap is the
planning anchor for retiring classical-only paths from the accept sets above.
