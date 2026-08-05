# Deployment hardening — Secret mounts

Section of `knowledge/security/deployment-hardening.md`.


The manifest references secrets by name from the platform store; values appear in no committed
file (SEC-020). Preference order for delivery into the process:

- Mounted secret files (tier 1): invisible in process listings and child-process environments,
  excluded from crash dumps of the environment block, and updatable in place — a file watch
  picks up rotation without a restart.
- Environment variables from the secret store: acceptable where the runtime offers nothing
  better, with the known costs — inherited by child processes, visible to same-pod inspection,
  serialized into diagnostics that dump the environment.
- Store-integrated injection (CSI/provider sidecar) follows the same file-mount properties;
  the platform layer owning it is named in the handoff.

Service-account tokens and cloud credentials follow workload identity
(`knowledge/security/secrets-and-keys.md#workload-identity`): the platform issues short-lived
identity to the workload; static cloud keys in manifests are the dominated option, carved out
only for platforms without federation, recorded in the handoff.
