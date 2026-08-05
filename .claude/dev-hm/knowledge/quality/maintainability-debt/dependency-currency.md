# Maintainability and technical-debt control — Dependency currency

Section of `knowledge/quality/maintainability-debt.md`.


Intake is governed by QUA-060 – QUA-063 (justified, alive, deterministic, unmodified generated
files); currency keeps "alive" true after intake. Integrity and provenance controls live in
`knowledge/security/supply-chain.md` and are not repeated here.

- Update on a cadence, not on a crisis: routine minor and patch updates land in a scheduled
  batch (weekly or per iteration, bot-assisted), so each upgrade stays small, reviewable, and
  bisectable. Security advisories preempt the cadence.
- Bound the lag: no direct dependency more than one major version behind its current major
  without a debt entry naming the blocker. The entry converts "we can't upgrade" from folklore
  into a tracked fact with an owner.
- Runtimes and frameworks follow their support windows. Running past end-of-support is a
  security and hiring problem wearing a maintainability costume; it is recorded as debt the day
  the window closes, not discovered during the next incident.
- Upgrade work is refactoring: parity rules apply, and a dependency major-version bump with
  behavior changes gets its own change with its own tests, never bundled into a feature.
