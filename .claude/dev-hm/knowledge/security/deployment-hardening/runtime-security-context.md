# Deployment hardening — Runtime security context

Section of `knowledge/security/deployment-hardening.md`.


The manifest states what the workload may do; the platform's pod security admission enforces a
namespace-level floor. Production namespaces enforce the restricted profile — the strictest of
the standard pod-security tiers — and the workload's own context matches it explicitly rather
than relying on defaults:

| Field | Value | Refuses |
|---|---|---|
| `runAsNonRoot` | `true` | a compromised entrypoint escalating to uid 0 |
| `allowPrivilegeEscalation` | `false` | setuid/file-capability privilege gains |
| `capabilities.drop` | `ALL` (add back only named, needed ones) | ambient kernel capabilities |
| `seccompProfile.type` | `RuntimeDefault` | syscalls outside the runtime's default set |
| `readOnlyRootFilesystem` | `true`, with writable mounts only at declared data paths | payload persistence and binary tampering in the container |

`privileged`, `hostNetwork`, `hostPID`, and `hostPath` mounts are one-way doors out of the
isolation model: each use is a declared, handoff-recorded exception with the reason and the
compensating control, never a convenience. Resource requests and limits are set on every
workload — an unbounded container is the noisy-neighbor availability failure
(`knowledge/security/resource-protection.md`). Where the platform team mutates workloads into
compliance automatically, the enforcement layer is named in the handoff; the manifest still
states its own context so the file reviews truthfully on its own.
