# Deployment hardening — Image construction

Section of `knowledge/security/deployment-hardening.md`.


The image is the attack surface the container starts with; build it empty-by-default:

- Minimal base: a distroless-style or otherwise stripped runtime base for production stages —
  no shell, no package manager, nothing to live off. Multi-stage builds keep compilers and
  build tooling in builder stages that never ship. The dominated option — a full OS base kept
  "for debugging" — is a legacy carve-out with a named reason; ephemeral debug containers cover
  the actual need without shipping the tooling.
- Non-root by default: the image declares a dedicated unprivileged user; nothing in the
  filesystem is owned or writable by it beyond declared data paths. Root-at-runtime is an
  exception the manifest and handoff both declare.
- Pinned parents: base images referenced by digest, not floating tags — a moving tag is an
  unreviewed change to everything above it (same rule as CI actions, SEC-064).
- No secrets in layers: build arguments and copied files persist in image history, so secrets
  never pass through either (SEC-020); build-time credentials use the builder's secret
  mounting, which leaves no layer. An ignore file keeps local env files and VCS metadata out of
  the build context.
- Scanned like dependencies: image scanning runs in CI with the same advisory gate as SEC-062,
  and the scan output is recorded with the build.
