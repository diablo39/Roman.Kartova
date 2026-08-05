# iSAQB practices: quality scenarios, trade-offs, architecture evaluation

The iSAQB CPSA-Foundation curriculum (current version per `knowledge/shared/versions.md`)
organizes the
architect's job around four tasks: clarify requirements, design the system, communicate and
document, and evaluate/analyze. This file distills the practices from that curriculum that this
plugin's architecture work uses directly. The Advanced level (CPSA-A) is a landscape of about
twenty independently versioned modules; the ones most relevant here are ADOC (architecture
documentation), ARCEVAL (architecture evaluation), DDD, FLEX (flexible architectures), WEB, and
API — useful search terms when a task needs depth this file doesn't carry.

## Quality goals and the ISO 25010 vocabulary

Name quality goals using the ISO/IEC 25010:2023 characteristics so they're comparable across
documents: functional suitability, performance efficiency, compatibility, interaction capability
(usability), reliability, security, maintainability, flexibility (portability), and safety
(added in the 2023 revision). Pick the top three to five for the system — more than five means
nothing is prioritized — and order them. The ordering is itself an architectural decision worth
an ADR when stakeholders disagree.

## Quality scenarios

A quality goal becomes testable when written as a scenario with six parts:

| Part | Question it answers |
|---|---|
| Source | Who or what triggers the situation? |
| Stimulus | What happens? |
| Environment | Under which operating conditions? |
| Artifact | Which part of the system is affected? |
| Response | What should the system do? |
| Response measure | How is success measured, with a number? |

Examples:

| Goal | Scenario |
|---|---|
| Performance | A logged-in user (source) submits a search (stimulus) at peak load of 500 concurrent users (environment); the search service (artifact) returns results (response) with p95 latency under 800 ms (measure). |
| Reliability | A node running the order service (artifact) crashes (stimulus) during normal operation (environment); processing resumes on another node (response) with no lost orders and recovery under 30 s (measure). |
| Maintainability | A developer new to the team (source) adds a payment provider (stimulus) in the current codebase (environment/artifact); the change ships (response) touching only the payments module, within three days (measure). |
| Security | An unauthenticated caller (source) replays a captured token (stimulus) against the public API (artifact); the request is rejected and logged (response) with zero successful replays (measure). |

Three scenario kinds cover most needs: usage scenarios (system in normal operation), change
scenarios (system being modified — these test maintainability and flexibility), and failure
scenarios (something breaks). Write at least one change scenario; designs are rarely wrong about
happy-path behavior and often wrong about where change lands. For security scenarios, derive
sources and stimuli from a threat model rather than inventing them — method in
`knowledge/security/threat-modeling.md`.

Scenarios do double duty: during design they rank options; after implementation the measures
become acceptance checks and SLO candidates.

## Design principles the curriculum leans on

Trade-off analysis chooses between options; these principles generate better options in the
first place. The foundation curriculum's design task rests on a compact set:

- Information hiding and encapsulation: expose contracts, hide decisions likely to change — the
  single strongest lever for cheap change scenarios.
- Separation of concerns; high cohesion within a building block, loose coupling between blocks.
  Coupling is what evaluation keeps finding: most failed change scenarios trace back to an
  unexpected coupling.
- Interfaces as contracts: design a building block's interface before its internals; the
  interface is what other teams and future changes depend on.
- Conceptual integrity: solve the same problem the same way everywhere; a mediocre convention
  applied consistently beats three clever local solutions.
- Simplicity (KISS) and YAGNI: prefer the simplest design that meets the current quality
  scenarios; flexibility no change scenario demands is cost, not value.
- Expect errors: design for failure at every boundary you don't control.
- Iterative, risk-first design: go breadth-first, then deep where scenarios show risk; top-down
  and bottom-up both have their place, and real design alternates between them.

When a design discussion stalls on taste, translate the disagreement into a quality scenario and
score the options: the scenario either exists (then it decides) or it doesn't (then the simpler
option wins).

## Trade-off analysis

Every architectural decision buys some qualities by spending others. Frequent tensions:

- Performance vs maintainability (caching layers, denormalization, hand-tuned code)
- Security vs usability and performance (extra auth hops, encryption overhead)
- Flexibility vs simplicity (plugin systems and abstraction layers vs direct code)
- Availability vs consistency (distributed data, CAP-shaped choices)
- Time-to-market vs almost everything (accepted debt must be recorded, not implied)

Working method for a decision:

1. State the decision to make and the quality scenarios it affects.
2. List two or three realistic options. An option set with one serious candidate and two straw
   men is not analysis; if only one option is realistic, say so and record why.
3. Score each option against the affected scenarios — a small table with +/o/− per scenario is
   enough; prose per cell where the judgment isn't obvious.
4. Name what the chosen option sacrifices. A decision record with no downside listed is
   incomplete by definition.
5. Note sensitivity points: assumptions that, if wrong, flip the choice ("chosen for <1M
   rows/day; revisit above that").

The output of steps 1–5 maps one-to-one onto a MADR record (see `adr-practice.md`: decision
drivers = scenarios, considered options, decision outcome with consequences).

## Architecture evaluation

Evaluation answers "will this architecture meet its quality goals?" — for a proposed design
before building, or an existing system before extending it. The iSAQB ARCEVAL module and the
ATAM tradition share one workhorse: scenario-based evaluation. Qualitative, cheap, and effective
at team scale:

1. Collect the quality goals and refine them into concrete scenarios (above). When many exist,
   organize them in a utility tree — goal → sub-goal → scenario — and rank each scenario by
   business value and by technical risk; evaluate high/high first.
2. Walk each high-priority scenario through the architecture views: which building blocks
   participate, what happens at each step, where does the scenario stress the design?
3. Record per scenario: the architectural approaches that address it, risks (reasons it might
   fail), non-risks (explicitly safe assumptions, so they're checkable later), sensitivity
   points (one quality depends heavily on one parameter), and trade-off points (one decision
   affects several qualities in different directions).
4. Summarize as a risk list ordered by impact, each risk with a mitigation or an explicit
   acceptance. This list feeds the phased plan — high risks get spikes or early phases.

Quantitative evaluation (load tests, static-analysis metrics, fitness functions) complements
the walkthrough where a response measure can be checked mechanically; prefer it whenever a
scenario's measure is cheap to automate. Full ATAM ceremony (multi-day stakeholder workshops)
is rarely warranted below organization-critical systems — the four steps above capture most of
the value.

Evaluate the architecture as documented and as built; where they differ, that gap is usually
the first risk on the list.

## Communicating the results

The foundation curriculum's third task — communicate and document — has one governing rule:
document for the reader's questions, not the writer's process. Concretely: quality scenarios and
constraints go where readers look for requirements (arc42 §1.2, §2, §10), decisions with their
trade-offs go in ADRs (arc42 §9 links to them), and views show only the abstraction level the
audience needs (see `c4-arc42.md`). Evaluation results land in arc42 §11 (risks and technical
debt) so the risk list survives the meeting where it was produced.

Sources: iSAQB CPSA-F curriculum (public.isaqb.org/curriculum-foundation), CPSA-A module list
including ARCEVAL and ADOC (isaqb.org), ISO/IEC 25010:2023; version anchors in
`knowledge/shared/versions.md`.
