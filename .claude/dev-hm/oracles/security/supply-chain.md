# Security oracle — Supply chain and dependencies

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-060 | Lockfile integrity | Every manifest change ships with the matching committed lockfile update; dependencies resolve from the lockfile at pinned versions | S1 | A03 · CWE-1357 · V15 | knowledge/security/supply-chain.md#lockfiles |
| SEC-061 | Registry pinning | New internal package references are namespace-scoped or registry-pinned so an identically named public package cannot shadow them; new dependency names verified against the intended project (no typosquat) | S1 | A03 · CWE-427 | knowledge/security/supply-chain.md#dependency-confusion |
| SEC-062 | Advisory scan | A dependency-audit tool was run on the change and reports zero known critical or high advisories in packages the diff adds or upgrades; tool and output recorded, or "not run" reported with reason; the verdict anchors to the recorded tool output at review time | S1 | A03 · CWE-1395 | knowledge/security/supply-chain.md#vulnerability-scanning |
| SEC-063 | Build ingestion | Build and CI scripts fetch artifacts over HTTPS with checksum or signature verification where the ecosystem supports it; zero curl-pipe-to-shell installs added | S2 | A03, A08 · CWE-494 | knowledge/security/supply-chain.md#build-and-ci-integrity |
| SEC-064 | CI actions pinned | Third-party CI actions and plugins added or changed are pinned to a full commit SHA or an exact immutable version, not a floating tag or branch | S2 | A03 · CWE-829 | knowledge/security/supply-chain.md#build-and-ci-integrity |
| SEC-065 | Vendored code provenance | Third-party source copied into the repository arrives with recorded provenance (upstream project, version or commit, license, retrieval URL); local modifications are itemized in the handoff | S2 | A03 · CWE-1104 | knowledge/security/supply-chain.md#vendored-code |
