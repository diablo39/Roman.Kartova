# Secrets and key management — Workload identity

Section of `knowledge/security/secrets-and-keys.md`.


The strongest secret is one that never exists. Prefer platform-issued, short-lived credentials
over stored static secrets wherever the platform offers them:

| Instead of | Use |
|---|---|
| Cloud API key in CI variables | OIDC federation: the pipeline proves its identity to the cloud provider and exchanges it for a scoped token valid for minutes |
| Database password in app config | Cloud IAM database authentication, or managed-identity token auth |
| Service-to-service shared secret | Platform workload identity (managed identities, service-account tokens, SPIFFE) — see `knowledge/security/transport-protection.md` for peer identity |
| Long-lived personal access token in automation | Short-lived installation or workload tokens scoped to the task |

Static secrets that must remain (third-party API keys, legacy systems) live in a secret manager
with access audit, not in the repository or CI variable lists copied by hand. Break-glass
credentials are vaulted, monitored, and tested rarely — not used as the everyday path.
