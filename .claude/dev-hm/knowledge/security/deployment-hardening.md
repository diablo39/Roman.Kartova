# Deployment hardening

The controls we ship in deployment artifacts — container images, orchestrator manifests, IaC —
so the runtime grants our code the least it needs and refuses what we did not sign. Deployment
files are code: diff-reviewed, oracle-checked (SEC-020 secrets in IaC, SEC-063/064 build
ingestion and CI pinning), and covered by policy tests that fail when a control is removed.
Build-side integrity — provenance, dependency discipline, CI — lives in
`knowledge/security/supply-chain.md`; how secret values reach the process is
`knowledge/security/secrets-and-keys/injection-into-the-process.md`; peer identity between
services is `knowledge/security/transport-protection/peer-identity.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Image construction | `knowledge/security/deployment-hardening/image-construction.md` |
| Runtime security context | `knowledge/security/deployment-hardening/runtime-security-context.md` |
| Secret mounts | `knowledge/security/deployment-hardening/secret-mounts.md` |
| Identity and network least privilege | `knowledge/security/deployment-hardening/identity-and-network-least-privilege.md` |
| Image provenance and admission | `knowledge/security/deployment-hardening/image-provenance-and-admission.md` |
| IaC discipline | `knowledge/security/deployment-hardening/iac-discipline.md` |
| Verification tests | `knowledge/security/deployment-hardening/verification-tests.md` |
