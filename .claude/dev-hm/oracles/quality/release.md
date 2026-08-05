# Quality oracle — Release readiness

Section of `oracles/quality-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-100 | Gate records aggregated at release | A release-bound change (version bump, release branch, deploy config) carries the aggregated security and quality gate verdicts of its constituent work items; zero release-bound changes without them | S1 | P7 | knowledge/quality/release-readiness.md#go-no-go |
| QUA-101 | Rollback plan stated | Deploy and release configuration changes state the rollback procedure (previous version, migration reversibility per QUA-091, flag kill-switch) in the handoff | S1 | ISO reliability (recoverability) | knowledge/quality/release-readiness.md#rollback |
| QUA-102 | Feature-flag lifecycle | New feature flags declare owner and removal condition where the repo documents flags; zero flags without a stated cleanup path (safe default: QUA-112) | S3 | ISO maintainability | knowledge/quality/release-readiness.md#flags |
| QUA-112 | Flag safe default | For each new feature flag, the value when the flag system is unreachable is the pre-change behavior; the default is visible in code or flag config | S2 | ISO reliability (recoverability) | knowledge/quality/release-readiness.md#flags |
