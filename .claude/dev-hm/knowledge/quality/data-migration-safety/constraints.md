# Data and schema migration safety — Constraints

Section of `knowledge/quality/data-migration-safety.md`.


Constraints are the cheapest data-quality control we have: a required field without them
accepts corrupt data silently, forever, and the cleanup is a migration of its own. Two rules
pull in opposite directions and both hold:

- New columns holding required business data carry their matching constraints — not-null
  with a stated default strategy, foreign keys, checks, uniqueness — at introduction, or the
  handoff states why not. "We validate in the application" is a reason to add the
  constraint, not to skip it; the database outlives any single writer.
- Constraints arrive on live tables without long exclusive locks. The cross-engine pattern
  is two-step: add the constraint unenforced for existing rows, then validate separately
  with a weaker lock (in PostgreSQL, `NOT VALID` followed by `VALIDATE CONSTRAINT`); enforce
  not-null by adding the column nullable, backfilling, then applying the constraint; build
  unique and other indexes concurrently before attaching the constraint that needs them.
  Engine specifics and lock tables: `knowledge/postgresql/schema-design.md#lock-safe-migrations`.

Defaults on new columns are cheap on modern engines (metadata-only in common cases) but
verify against the engine and version in play rather than assuming.
