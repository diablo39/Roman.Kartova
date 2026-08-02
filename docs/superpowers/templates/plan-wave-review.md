# Plan wave review — agent review of an implementation plan before execution

**Status:** pilot — both waves run 2026-08-02 against the A2 System-list-surface plan. Not yet a working agreement; adopt, amend, or drop after a second slice, per the rule in §7.

**Pilot result in one line:** 9 agents, 1.31M tokens, ~19 min wall-clock, on a ~250-LOC plan → 5 upheld blocking findings including one query shape that would have 500'd in production, one that would have shipped silent row-skipping on the Services list, and one defect the *first* wave itself introduced.

Reviews the **plan**, before any code exists. Does **not** substitute for DoD gate 2 (per-task reviews) or gates 6–8 (post-code reviews) — different artifact, different failure classes, no folding.

---

## 1. Where it sits

| | |
|---|---|
| Runs on | the written plan + its spec, **before** Task 0 |
| Reviews | task decomposition, planned production snippets, planned test list, sequencing, impact analysis |
| Home | an extension of `superpowers:writing-plans` self-review — **not** a numbered DoD gate |
| Output | findings only; no agent writes code or edits files |

Why not a numbered gate: the ten DoD gates are all about code that exists and are re-runnable against the final commit. A plan review is neither. Making it a gate would also force a renumber of a list that was just flattened.

---

## 2. Roster

**Wave 1 — design & feasibility.** Runs first: it can invalidate the test plan.

| Agent | Lens |
|---|---|
| `solution-architect` | ADR conformance, read-model shape, task decomposition |
| `csharp-senior-architect` (or the language equivalent) | will the planned code actually compile/translate; idiom |
| `postgresql-expert` | index reality, query plans, RLS interaction — **only when the plan adds a SQL predicate, join, or migration** |
| `senior-security-engineer` | tenant isolation, authorization, control-verification tests — **only when the plan touches a trust boundary** |
| `typescript-senior-dev` | frontend feasibility, component/test conventions — **only when the plan has a UI surface** |

**Wave 2 — test-plan quality.** Runs against the *revised* plan.

| Agent | Lens |
|---|---|
| `general-purpose` + skill `dotnet-test:test-gap-analysis` | pseudo-mutation over the planned production snippets — would the planned tests catch a dropped clause, a flipped boundary, a swapped join key? Closest static replacement for the removed mutation gate. |
| `general-purpose` + skill `dotnet-test:test-anti-patterns` | assert-nothing / tautological / order-dependent tests in the planned list |
| `general-purpose` + skill `dotnet-test:grade-tests` | per-test A–F table over the named test cases |
| `senior-quality-engineer` | the plan as a whole against the quality oracle: adequacy, evidence, release readiness |

**Deliberately excluded** (record the reason, don't silently drop): `csharp-code-reviewer` / `typescript-code-reviewer` review *diffs* — redundant pre-code, and gate 2 owns that. `assertion-quality` overlaps `test-anti-patterns`. `find-untested-sources`, `coverage-analysis`, `crap-score`, `run-tests`, `test-quality-auditor` all need real code or coverage data and cannot run pre-implementation.

**Roster scaling:** ≤200 LOC of production code → wave 2 only. >200 LOC, or any new SQL predicate / migration / trust boundary → both waves, adding the conditional agents that apply.

---

## 3. Input packet — identical for every agent

```
- the plan under review (absolute path)
- the spec section it implements (source of truth)
- CLAUDE.md, docs/TESTING-STRATEGY.md, the ADRs the plan cites
- the predecessor slice's commit (`git show <sha> --stat`) when the plan mirrors it
```

Plus the review contract, stated in every prompt:
- **anchor every finding to `Task N / Step M`** — a plan has no file:line;
- **write no code into the repo and edit no files** (reading code to verify claims is required);
- each agent gets its own **numbered lens questions**, not a generic "review this".

---

## 4. Output contract (mergeable across agents)

```
### Blocking          — plan produces wrong/unshippable work if executed as written
### Should-fix        — works, but a materially better approach exists
### Nits              — cap 5
### Missing tasks     — spec requirement or failure mode with no task covering it
### What looks right  — mandatory, 3-5 items (an all-negative review is uncalibrated)
```

Per finding: **Title** · **Anchor** · **Evidence** (the ADR/spec/file:line actually checked) · **Impact** · **Fix** (concrete plan change).
Findings without an anchor and a concrete fix are discarded at triage.

---

## 5. Execution

```
Wave 1 (parallel, one message)
  → triage → apply accepted fixes → commit "plan: wave-1 review fixes"
Wave 2 (parallel, one message, against the REVISED plan)
  → triage → apply → commit "plan: wave-2 review fixes"
  → Task 0
```

Two waves rather than one fan-out: wave 1 can change the design, which would invalidate wave 2's analysis of the test plan.

**Triage is not delegated.** A finding counts as real only when verified against the codebase or a cited ADR — agents contradict each other and each other's evidence (see §6 for a worked example). Duplicate findings merge with an `agreed_by` count.

---

## 6. Run log

### Wave 1 — 2026-08-02 · plan `2026-08-02-catalog-system-list-surface-a2.md`

Five agents, dispatched in a single message, run in parallel.

| Agent | Model | Tokens | Tool uses | Duration | Blocking | Should-fix | Nits | Missing |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `solution-architect` | opus | 147,912 | 40 | 11m 12s | 2 | 6 | 5 | 4 |
| `csharp-senior-architect` | sonnet | 148,355 | 52 | 8m 57s | 1 | 4 | 2 | 1 |
| `senior-security-engineer` | opus | 142,214 | 28 | 8m 00s | 2 | 4 | 5 | 6 |
| `typescript-senior-dev` | sonnet | 134,595 | 32 | 5m 27s | 0 | 4 | 5 | 3 |
| `postgresql-expert` | sonnet | 113,411 | 22 | 6m 49s | 1 | 2 | 5 | 3 |
| **Total** | | **686,487** | **174** | **11m 12s wall** | **6** | **20** | **22** | **17** |

Wall-clock is the slowest agent, not the sum — the fan-out is genuinely parallel. Sequential execution would have cost ~40 minutes.

**Cost observations for the adoption decision:**
- Token spend is remarkably flat across agents (113k–148k, σ ≈ 14k) and appears driven by the shared input packet plus each agent's own repo reading, not by the depth of its lens or its model tier. `postgresql-expert` was cheapest with the fewest tool uses (22) yet produced a blocking finding.
- Model tier is set by each agent's own frontmatter, not by the dispatcher: `solution-architect` and `senior-security-engineer` are **opus**, the other three **sonnet**. Two opus agents account for 290k of the 686k tokens, so the wave's *cost* is weighted well above its token share — worth pricing before the roster grows.
- Tool uses vary 22–52 with no correlation to findings volume; `csharp-senior-architect` used the most (52) for one blocking finding, but that finding was the highest-value one in the wave (see below).
- ~686k tokens to review a ~250-LOC plan is disproportionate on its face. The justification is entirely in whether the findings were real and unique — measure, don't assume.

**Disagreement worth recording** (the reason triage cannot be delegated): three agents reached three different verdicts on the same line of planned code, `rows.ToDictionary(x => x.ComponentId, …)` in Task 2 —
`postgresql-expert` called it **Blocking** (legacy duplicate `PartOf` rows in an un-migrated database → `ArgumentException` → 500);
`senior-security-engineer` called it **safe** (the partial unique index makes duplicates impossible);
`csharp-senior-architect` called it **safe for a different reason** (the migration `DELETE`s duplicates before creating the index).
All three cite real evidence. Resolving it requires deciding whether "a database where the migration has not run" is in scope — a judgement call, not a lookup. **Resolution:** the migration collapses duplicates and creates the index in one transaction, and ADR-0085 runs the migrator as a pre-upgrade Job, so no un-migrated database serves traffic. Downgraded to a nit; `.DistinctBy` adopted anyway because it costs one call.

**Wave-1 triage outcome:** 3 of 6 blocking findings upheld as blocking, 2 downgraded to should-fix/nit, 1 (case 7) upheld at a different severity. 14 should-fix applied, 2 deferred with a recorded reason. The plan grew from 12 tasks to 15.

### Wave 2 — 2026-08-02 · same plan, after wave-1 revisions

Three `dotnet-test` **skills** run inside `general-purpose` agents, plus one dev-hm agent. (The dotnet-test plugin's own *agents* — builder/tester/fixer/implementer/auditor — need real code or coverage data and cannot run pre-implementation; its value at plan time is entirely in the skills.)

| Agent / skill | Source | Model | Tokens | Tool uses | Duration | Blocking | Should-fix | Nits | Missing |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| `test-gap-analysis` | dotnet-test | sonnet | 151,348 | 25 | 7m 31s | 3 | 6 | 5 | 7 |
| `test-anti-patterns` | dotnet-test | sonnet | 155,157 | 19 | 7m 47s | 2 | 8 | 5 | 9 |
| `grade-tests` | dotnet-test | sonnet | 170,180 | 21 | 5m 28s | — | — | — | 5 |
| `senior-quality-engineer` | dev-hm | sonnet | 149,239 | 31 | 5m 06s | 0 | 3 | 5 | 2 |
| **Total** | | | **625,924** | **96** | **7m 47s wall** | **5** | **17** | **15** | **23** |

`grade-tests` emits a per-test A–F table rather than severity-tiered findings, so its blocking/should-fix columns are not applicable; it graded 21 planned tests (8 A · 3 B · 7 C · 3 D · 0 F) and named five coverage gaps.

**Both waves: 1,312,411 tokens, 270 tool uses, ~19 minutes of wall-clock across 9 agents.**

**What wave 2 actually bought — the strongest evidence in this pilot:**
- It caught a **defect wave 1 introduced**. Wave 1 correctly identified that two unit tests were vacuous and prescribed `Limit: 1`; the prescribed fix was itself wrong, because the fixture seeds one Active and one Decommissioned app and the default lifecycle view hides the second. Three wave-2 agents found this independently. No amount of re-running wave 1 would have caught it — reviewing the *revision* is a different act from reviewing the original.
- It found two mutants with **no test at any tier** (the Services-side f-map, and the f-map's de-dup/ordering) that four design-lens agents had all missed.
- It corrected two "this test pins that branch" claims that were **equivalent mutations** — false coverage claims that would otherwise have been inherited into `gate-findings.yaml` as fact.

**Cost comparison:** wave 2 cost 91% of wave 1 (626k vs 686k tokens) for 3 fewer agents, and produced a comparable finding count with a higher proportion of *unique* findings. Tokens per agent are again flat (149k–170k), reinforcing that spend tracks the input packet and repo reading, not the lens.

---

## 7. Telemetry and the adoption rule

Per slice: `docs/superpowers/verification/<slice>/plan-review-findings.yaml`, same shape as `gate-findings.yaml` plus three fields:

```yaml
- agent: postgresql-expert
  wave: 1
  severity: blocking | should-fix | nit
  verdict: real | delusion
  unique: true            # no other agent in the wave raised it
  anchor: "Task 2 / Step 1"
  tokens: 113411          # per-agent cost for the run that produced it
  title: "..."
  note: "applied in <sha> | rejected because ..."
```

**Adoption rule, evaluated after ≥2 slices:**

| Outcome | Action |
|---|---|
| ≥1 **unique real** blocking/should-fix finding | agent stays in the mandatory roster |
| Real findings, all duplicated by another lens | demote to conditional |
| Precision <30% (real ÷ raised), or zero real findings | drop from the roster, record why |

**Kill criterion for the whole process:** if no plan-review finding prevents a later gate 6/7/8 finding, the wave review is pure cost. Track this explicitly — it is the honest way to end the experiment.

**Cost gate:** record total tokens per wave. If a slice's plan review costs more than the implementation itself and yields no unique blocking finding, that is evidence for shrinking the roster, not for running it harder.

---

## 8. Known limitations

- **Reproducibility:** the dev-hm agents live in `.claude/agents/` and resolve their oracles to `test-plugin/` — **both untracked in git today**. Until they are committed, this process only works on one machine.
- Agents will assert things about code that does not exist yet; the anchor + concrete-fix requirement is what makes those assertions checkable.
- The `What looks right` section is mandatory precisely because an agent that only finds problems gives no signal about what it verified and accepted.
