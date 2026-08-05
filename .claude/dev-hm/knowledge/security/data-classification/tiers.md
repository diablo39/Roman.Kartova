# Data classification — Tiers

Section of `knowledge/security/data-classification.md`.


| Tier | Name | What it covers | Examples | Baseline handling |
|---|---|---|---|---|
| 1 | Public | Published or intended for publication; disclosure is harmless | docs, marketing content, public API schemas, open-source code | integrity and provenance still matter — tampering with public data is its own harm |
| 2 | Internal | Business information not for outsiders; disclosure aids a competitor or an attacker's reconnaissance | internal metrics, architecture documents, non-sensitive user content, opaque internal identifiers | authenticated access, encrypted transport, platform at-rest encryption |
| 3 | Confidential-regulated | Data whose mishandling harms a person or breaches law or contract | personal data (GDPR), payment and cardholder data (PCI DSS), health data, credentials and keys, session artifacts | every control in this file; credentials additionally follow `knowledge/security/secrets-and-keys.md` |

The rules that make the scheme enforceable in code:

- Classification attaches at the field level, not the table or service level — one Tier-3 column
  does not make a whole database Tier 3, and a "mostly internal" table does not dilute the
  column that is not.
- Every new persisted field, message schema, or store carrying Tier-3 data declares its tier: a
  schema annotation, a data-catalog entry, or a handoff line. Undeclared means unreviewable.
- When unsure, classify up; reclassifying down later is a review decision, leaking meanwhile is
  not recoverable.
- Aggregation can raise the tier: a single coarse location is Tier 2; a movement trail is
  Tier 3. Classify the assembled dataset, not only its parts.
- In code, Tier-3 values ride typed wrappers where the stack allows, the same mechanism as the
  secret types in `knowledge/security/secrets-and-keys.md` — so masking and redaction follow the
  value through the program instead of relying on every call site remembering.
