# Review method — Severity-tiered review procedure

Section of `knowledge/quality/review-method.md`.


1. Scope. Enumerate changed files and classify the change (feature, fix, refactor, dependency,
   config, CI). The classification activates oracle sections: a refactor triggers QUA-006; a
   manifest change triggers the dependency and supply-chain sections; a new endpoint triggers
   the full security walk.
2. Understand intent before code. Read the work-package objective or commit intent first;
   review is comparing the diff against intent and against the oracles, in that order. A diff
   that does something other than its stated intent is a finding regardless of code quality.
3. Walk the oracle. Evaluate every applicable SEC-*/QUA-* core entry plus the stack addendum
   (`oracles/addenda/<stack>.md`), each against its pass criterion, independently of anyone
   else's verdicts. Order the walk S0-first: correctness gates (build, tests), then security
   sections, then the rest — a red build makes further verdicts provisional.
4. Review beyond the oracle. Judgment concerns — design fit, naming, altitude, missing
   abstraction — become `finding` lines with a severity assigned from the tier definitions and
   a one-line justification. Judgment findings never borrow oracle IDs.
5. Verify, don't assume. Run what you can (build, tests, linter); what you cannot run is
   reported "not verifiable from the diff — needs X", never passed on trust
   (`knowledge/shared/ground-rules.md`).
6. Report in the shared format (below), severity-ordered, condensed.

Time allocation follows risk (ISTQB P2, P4): new external interactions, authorization logic,
money/data-deletion paths, and concurrency get the slow read; mechanical renames get a fast one.
