# Secrets and key management — Injection into the process

Section of `knowledge/security/secrets-and-keys.md`.


How a secret travels from store to running code, in order of preference:

- Mounted secret files or environment variables set by the orchestrator at deploy time, sourced
  from the platform secret store. The manifest references the secret by name; the value never
  appears in any file we commit.
- SDK fetch at startup from the secret manager, cached in memory with a refresh interval so
  rotation propagates without redeploy.

Never: baked into container images or build arguments (image history retains them), committed
`.env` files (a `.env.example` with placeholders is the committed artifact), or anything shipped
to a browser or mobile client — code delivered to the user's device is public, and a secret it
contains is published, not stored.
