# Test data management — The PII boundary

Section of `knowledge/quality/test-data-management.md`.


The line between what security owns and what this file asks of tests:

- Classification, masking mechanics, telemetry hygiene, and retention are defined in
  `knowledge/security/data-classification.md`. This file's rule is the
  consequence: Tier-3 data (personal, payment, health, credentials) does not enter test
  environments, fixtures, recorded HTTP cassettes, or seed files.
- Pseudonymized is not anonymized. Data with names hashed or IDs swapped remains personal data
  under GDPR as long as any party can re-link it; current EU guidance (EDPB pseudonymisation
  and anonymisation guidelines, editions per
  `knowledge/shared/versions.md`) applies a three-part identifiability
  test — singling out, linkability, inference — and most "masked" extracts fail it. A test
  environment holding pseudonymized production data therefore inherits every Tier-3 control:
  restricted access, telemetry hygiene, retention, residency. That cost almost always exceeds
  the cost of synthesizing.
- Fixtures never contain real credentials, API keys, or tokens — a real secret in a fixture is
  a hardcoded secret (SEC-020, S0) regardless of directory name. Synthetic secrets should be
  obviously synthetic (a `TEST-` prefix or documented dummy value) so secret scanners can be
  tuned without allowlisting real-looking strings.
- Synthetic markers are a feature: fixtures built from card-shaped numbers that pass checksum,
  fake-domain emails, and known-fake identifiers double as the canary values the telemetry and
  masking tests look for
  (`knowledge/security/data-classification.md#verification-tests`).
- Incident reproduction is the tempting exception: copying the production record that broke
  things into a fixture is fast and usually unlawful. Reproduce by shape — rebuild the record's
  structure with synthetic values, confirm it still triggers the defect — and the regression
  test is shareable forever
  (`knowledge/quality/incident-quality-loop.md`).
