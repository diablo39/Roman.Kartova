# Review method — License policy

Section of `knowledge/quality/review-method.md`.


What makes QUA-061's "license compatible with the project" decidable. Licenses are identified
by their SPDX identifiers (the industry-standard short IDs from the SPDX License List — MIT,
Apache-2.0, GPL-3.0-only; spec and list state per `knowledge/shared/versions.md`), including
expressions (`Apache-2.0 OR MIT` — either side may be chosen, record which;
`GPL-2.0-only WITH Classpath-exception-2.0` — the exception changes the obligations). Verify
the identifier against the package's actual LICENSE text when anything rides on it: manifest
metadata is a claim, not evidence. Obligation classes:

| Class | Representative SPDX IDs | Obligation when we use it |
|---|---|---|
| Permissive | MIT, Apache-2.0, BSD-2/3-Clause, ISC, Zlib | Attribution/notice preservation; Apache-2.0 adds a patent grant and NOTICE handling |
| Weak copyleft | MPL-2.0, LGPL-2.1/3.0, EPL-2.0 | Modifications to the covered files/library must be shared; our own code linking to it is not captured (LGPL: keep it replaceable — dynamic linking or equivalent) |
| Strong copyleft | GPL-2.0, GPL-3.0 | Distributing a derived work requires distributing its source under the same license |
| Network copyleft | AGPL-3.0 | Copyleft triggers on network use, not only distribution — SaaS backends are not exempt |
| Source-available / restricted | BUSL-1.1, SSPL-1.0, Elastic-2.0, "fair-source" and non-commercial variants | Not open source; usage limits (competition clauses, user caps, field-of-use) — individual legal review, never allowlisted by class |
| Public-domain-equivalent | CC0-1.0, Unlicense, 0BSD | No obligations |
| Unclear | No license file, custom text, `NOASSERTION` from a scanner | Undecidable — treat as deny until resolved; no license means no grant |

The policy that makes the check deterministic is an allowlist committed to the repository
(policy-as-code where tooling exists: cargo-deny licenses config, a checker config in the
package manifest, or an ORT/ScanCode-class scanner policy): allowed identifiers, denied
identifiers, and everything else routed to review — deny-by-default, because a license nobody
evaluated is not thereby fine. Two facts shape any sound policy: obligations depend on the
distribution model (a shipped binary or mobile app triggers distribution clauses that a
server-side service does not — AGPL bites regardless), so the policy states which model it
assumes; and the check covers the transitive tree, not just direct additions — one copyleft
transitive obligates the same as a direct one. The reviewer's QUA-061 procedure: identifier on
the allowlist → pass; on the denylist → fail; anything else → `finding` for a policy decision,
recorded so the next occurrence is decidable. License changes on upgrade (a dependency
relicensing from permissive to BUSL/SSPL on a version bump) re-run the same check — the
security half of that event is covered in `knowledge/security/supply-chain.md`.
