# Software supply chain security

Reviewing dependency, build, and CI changes — the territory of OWASP Top 10:2025 A03 Software
Supply Chain Failures (new in 2025) and oracle entries SEC-060 – SEC-065. Section anchors match
the remediation pointers in `oracles/security-oracle.md`. Framework versions cited here are
anchored in `knowledge/shared/versions.md`.

The unit of review is any diff touching a manifest, lockfile, build script, CI workflow, Docker
base image, or vendored code. Such changes execute other people's code in your build and runtime
with your privileges; review them with the same seriousness as handler code.

## SLSA

SLSA (Supply-chain Levels for Software Artifacts; current edition per
`knowledge/shared/versions.md`) defines two tracks of graduated requirements. Use the levels as
a shared vocabulary for what a pipeline does and does not guarantee — most oracle entries map
to the properties Build L2/L3 and Source L2 assume.

| Track | Level | Guarantee |
|---|---|---|
| Build | L0 | No requirements — no provenance |
| Build | L1 | Provenance exists, showing how the package was built; unforgeable is not claimed |
| Build | L2 | Provenance signed by a hosted build platform; forging requires platform compromise |
| Build | L3 | Hardened builds: isolated build environments, provenance resistant to tampering by the build itself |
| Source | L1 | Version controlled: immutable, uniquely identifiable revisions in a modern VCS |
| Source | L2 | History and provenance: continuous, immutable branch history; no force-push rewrites; source provenance attestations |
| Source | L3 | Continuous technical controls enforced for protected refs and recorded in attestations |
| Source | L4 | Two-party review: changes to protected branches agreed by two or more trusted persons |

Review implications: consuming a dependency that publishes SLSA Build provenance lets you verify
the artifact was built from the claimed source by the claimed builder — prefer such dependencies
where alternatives are equal. Producing provenance is a pipeline concern; when a diff changes
release workflows, do not degrade an existing provenance or signing step without a recorded
decision.

NIST SSDF (SP 800-218) is the umbrella practice framework — four groups: Prepare the
Organization (PO), Protect the Software (PS), Produce Well-Secured Software (PW), Respond to
Vulnerabilities (RV). The oracle's supply-chain entries operationalize the PS/PW practices that
are checkable per-diff; the edition to cite is per `knowledge/shared/versions.md`.

## Lockfiles

Supports SEC-060. The lockfile is the boundary between "versions we reviewed" and "whatever the
resolver picks today". Every manifest change ships with the matching lockfile update in the same
diff, and CI installs from the lockfile in strict mode:

| Ecosystem | Lockfile | Strict install |
|---|---|---|
| npm/pnpm/yarn | package-lock.json / pnpm-lock.yaml / yarn.lock | `npm ci`, `pnpm install --frozen-lockfile`, `yarn install --immutable` |
| Python (uv) | uv.lock | `uv sync --locked` |
| Rust | Cargo.lock | `cargo build --locked` |
| Go | go.sum | verified automatically on build |
| Java (Gradle) | gradle.lockfile (dependency locking on) | `--write-locks` only on intended updates |
| .NET | packages.lock.json (`RestorePackagesWithLockFile`) | `dotnet restore --locked-mode` |
| Flutter/Dart | pubspec.lock | `dart pub get --enforce-lockfile` |

Review cues: a manifest edit without its lockfile fails SEC-060; a lockfile churn far larger
than the manifest change means transitive drift — read what actually changed. Hand-edited
lockfiles fail QUA-063; regenerate with the tool. Wildcard or `latest` ranges in the manifest
fail QUA-062 even when a lockfile exists.

## Dependency confusion

Supports SEC-061. Two related attacks on name resolution:

- Dependency confusion: an attacker publishes a package on the public registry with the same
  name as your internal package and a higher version; misconfigured resolvers prefer it.
  Defenses in review: internal packages are namespace-scoped (`@volue/…` npm scopes, reserved
  prefixes) or the resolver pins each package to one registry (npm scoped registries, pip
  `--index-url` with a locked-down proxy, NuGet package source mapping, Maven repository
  ordering that never falls through to public for internal groupIds).
- Typosquatting: a new dependency named one edit away from the intended one (`requets`,
  `colour-json`). For every dependency added, verify name, homepage, and repository match the
  intended project before accepting; record the verification in the handoff.

A new dependency whose resolution path could be shadowed by an identically named public package
fails SEC-061 until the scope or registry pin is in place.

## Vulnerability scanning

Supports SEC-062. Run the ecosystem's audit tool on any diff that adds or upgrades packages, and
record tool plus result in the handoff: `npm audit` / `pnpm audit`, `uv run pip-audit`,
`cargo audit`, `govulncheck`, OWASP Dependency-Check or `dotnet list package --vulnerable`,
Trivy or Grype for container images. Pass means zero known critical or high advisories in the
packages the diff adds or upgrades; a finding that cannot be fixed by bumping goes through the
waiver flow with the exposure reasoned about explicitly (is the vulnerable code path reachable?).
"Not run" with a reason is honest and reviewable; an unrecorded scan is not evidence (see
`knowledge/shared/ground-rules.md`). Scanning covers direct and transitive dependencies — the
lockfile is the input, not the manifest.

## Build and CI integrity

Supports SEC-063, SEC-064. Build scripts and CI workflows are code that runs with credentials;
review their diffs accordingly.

- Artifact ingestion (SEC-063): everything fetched during build arrives over HTTPS and is
  verified by checksum or signature where the ecosystem supports it. `curl … | sh` added to a
  Dockerfile or CI step fails; download, verify a pinned SHA-256, then execute.
- Action pinning (SEC-064): third-party CI actions and plugins are pinned to a full commit SHA
  (`uses: some/action@8f4b7f8…`), not a floating tag or branch. Tags are mutable; a compromised
  upstream retags and your pipeline runs attacker code with repository secrets.
- Watch for privilege drift in the same diffs: broadened workflow permissions, secrets exposed
  to fork-triggered runs (`pull_request_target` with checkout of the PR head), or new steps that
  echo the environment.
- Scripts executed at dependency install time (npm postinstall and equivalents) run on every
  developer machine and CI node; a new dependency that adds install scripts deserves a look at
  what those scripts do — prefer `--ignore-scripts` installs where the ecosystem tolerates it.

## Vendored code

Supports SEC-065. Copying third-party source into the repository trades registry risk for
maintenance risk: no resolver, no advisories, no upgrade path unless provenance is recorded.
When vendoring is the right call (patched fork, registry unavailable, single small file),
require alongside the code: upstream project name, exact version or commit, license, retrieval
URL, and an itemized list of local modifications. Unattributed vendored code fails SEC-065 —
it cannot be scanned (its CVEs are invisible to SEC-062 tooling), its license obligations are
unknown, and the next maintainer cannot tell local fixes from upstream. In this repository the
same principle appears as `UPSTREAM.md` files for imported plugins.

## SBOM

A software bill of materials makes the dependency inventory explicit and machine-readable —
the input for answering "are we affected?" when the next major advisory lands. Two interchange
formats dominate: CycloneDX (OWASP) and SPDX (ISO/IEC 5962); generators include `syft`,
`cyclonedx-*` ecosystem tools, and native support in some build systems. Where the pipeline
already produces an SBOM or provenance attestation, treat it like the signing step above: a diff
that silently drops or bypasses it needs a recorded decision, not a quiet deletion. SBOMs
complement, not replace, lockfiles — the lockfile pins what will be installed; the SBOM records
what was shipped.

## Review walk-through for a dependency-adding diff

1. Manifest and lockfile change together, versions exact (SEC-060, QUA-062).
2. Name verified against the intended project; internal names scoped/pinned (SEC-061).
3. Audit tool run and recorded; criticals/highs resolved or waived (SEC-062).
4. Justification recorded: why this package, what alternative was rejected (QUA-060); alive and
   license-compatible (QUA-061).
5. No new install scripts doing surprising work; no new curl-pipe-to-shell (SEC-063).
6. CI changes pinned by SHA; permissions not broadened (SEC-064).
7. Vendored copies carry provenance (SEC-065).
