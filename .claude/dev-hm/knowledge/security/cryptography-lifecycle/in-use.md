# Cryptography lifecycle — In use

Section of `knowledge/security/cryptography-lifecycle.md`.


Data being processed is plaintext somewhere; the controls bound where and for how long:

- Decrypt at the point of use, not at load; keep plaintext lifetime and scope minimal. Zeroize
  buffers where the language allows it, and treat zeroization honestly: garbage-collected
  runtimes cannot guarantee erasure, so the effective control there is scope minimization plus
  the secret-typed wrappers from `knowledge/security/secrets-and-keys.md`.
- Keep plaintext out of failure artifacts: crash reporters strip memory contents and local
  variables; core dumps are disabled or encrypted for services processing Tier-3 data.
- Confidential computing, when the threat model includes the host: TEE-backed confidential
  workloads encrypt memory against the infrastructure operator and attest their identity. The
  control shape to build is attestation-gated key release — the KMS releases data keys only to
  an environment that passes attestation, so an unattested copy of the workload cannot decrypt
  anything. Adopt this for Tier-3 processing on infrastructure outside our control; for
  first-party infrastructure, process isolation plus memory hygiene is the honest baseline.

Verification: the attestation policy gets the refusal treatment — a deploy-environment test
asserts key release fails for an unattested or mis-attested environment, not only that it
succeeds for the right one.
