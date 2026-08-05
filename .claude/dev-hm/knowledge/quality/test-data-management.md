# Test data management

Where test data comes from, what it may legally and safely contain, and how it stays true to
production shape. Test adequacy (`knowledge/quality/test-adequacy.md`)
judges whether tests bite; this file supplies the data policy underneath them — a suite is only
as honest as the data it runs on. The classification and masking rules the policy leans on are
owned by the security side (`knowledge/security/data-classification.md`);
this file states the quality-side consequences. Regulatory guidance editions cited here are
anchored in `knowledge/shared/versions.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Sourcing tiers | `knowledge/quality/test-data-management/sourcing-tiers.md` |
| Fixtures and seeding | `knowledge/quality/test-data-management/fixtures-and-seeding.md` |
| Determinism | `knowledge/quality/test-data-management/determinism.md` |
| The PII boundary | `knowledge/quality/test-data-management/the-pii-boundary.md` |
| Fixture drift | `knowledge/quality/test-data-management/fixture-drift.md` |
| Volume and performance data | `knowledge/quality/test-data-management/volume-and-performance-data.md` |
| What the gate asks | `knowledge/quality/test-data-management/what-the-gate-asks.md` |
