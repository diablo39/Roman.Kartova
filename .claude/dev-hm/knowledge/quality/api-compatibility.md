# API compatibility and versioning

What makes a change to a published interface breaking, how versions and deprecations are
managed, and how compatibility is tested against the clients that actually exist. This file
extends `knowledge/quality/test-adequacy/contract-tests.md` (which covers
how contracts are pinned) with the policy for evolving them; it backs QUA-017 (contract test
moves with contract), QUA-090 (expand-contract, the schema twin of this file), and QUA-113
(configured compatibility checks run on contract changes). A "published" interface is one with
consumers you cannot atomically update: another team's service, a mobile app in the field, a
partner integration, a library's downstream users. Interfaces whose consumers all live in the
same repository and deploy together are refactoring territory, not compatibility territory.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Breaking-change taxonomy | `knowledge/quality/api-compatibility/breaking-change-taxonomy.md` |
| Compatibility direction | `knowledge/quality/api-compatibility/compatibility-direction.md` |
| Versioning policy | `knowledge/quality/api-compatibility/versioning-policy.md` |
| Deprecation policy | `knowledge/quality/api-compatibility/deprecation-policy.md` |
| N-1 client tests | `knowledge/quality/api-compatibility/n-1-client-tests.md` |
| Tooling | `knowledge/quality/api-compatibility/tooling.md` |
| What the gate asks | `knowledge/quality/api-compatibility/what-the-gate-asks.md` |
