# Test data management — Sourcing tiers

Section of `knowledge/quality/test-data-management.md`.


The tier-1 source for new test suites is constructed data: values built in the test, by
builders and factories, or generated with a seeded generator. Constructed data is safe by
default — it carries no personal data, no residency constraint, no masking obligation — and it
is precise: each test states exactly the shape it needs, no more.

| Source | Standing | Properties |
|---|---|---|
| Built-in-test values and builders | Tier 1 — default | Exact, minimal, self-documenting; the test shows its own preconditions |
| Seeded synthetic generators (faker-class libraries, property-based generators) | Tier 1 — for breadth | Volume and variety without provenance risk; seeds logged for replay |
| Statistically-shaped synthetic data (generated to match production distributions) | Tier 1 — for realism | Production-like skew for performance and migration tests without production content |
| Anonymized production extracts | Legacy carve-out | Only when the conditions below hold |
| Raw or pseudonymized production data | Never in test environments | Still personal data; see the PII boundary below |

The carve-out, stated as checkable conditions: a production extract may feed a test environment
only when (1) the transformation pipeline that produced it pre-exists the diff and is owned per
the masking rules in `knowledge/security/data-classification.md#masking`,
(2) the output has been assessed as anonymized — not merely pseudonymized — under the
identifiability tests referenced in the PII boundary section, and (3) the handoff records why
synthetic data cannot expose the defect class under test (typically: a bug reproducible only on
real-world distribution or referential tangles). The migration path back to the default is
statistically-shaped synthesis: measure the distributions the test needs and generate data that
matches them.
