# Architecture evaluation: utility trees, ATAM-lite, fitness functions, drift

This file deepens the four-step scenario walkthrough in `isaqb-concepts.md` into a repeatable
evaluation practice: building a utility tree, running a scaled-down ATAM session, automating
response measures as fitness functions in CI, and detecting drift between the architecture as
documented and as built. Method sources are the SEI's ATAM tradition and the iSAQB ARCEVAL
module; the evolutionary-architecture vocabulary (fitness functions) comes from Ford, Parsons,
Kua, and Sadalage, *Building Evolutionary Architectures* (current edition per
`knowledge/shared/versions.md`).

## The utility tree — worked example

A utility tree organizes quality scenarios so evaluation effort lands where it matters. Root is
"utility" (overall goodness), first level the ISO 25010 characteristics relevant to this system,
second level refinements, leaves concrete scenarios. Each leaf is ranked twice, high/medium/low:
business value (what it costs the business if this scenario fails) and technical risk (how
unsure we are that the architecture meets it). Evaluate (H,H) leaves first; (L,L) leaves not at
all.

Example for an order-processing service being extended with a new payment provider:

```
Utility
├── Performance efficiency
│   ├── Throughput
│   │   └── (H,M) Checkout completes at p95 < 800 ms at 500 concurrent users
│   └── Resource behavior
│       └── (M,L) Nightly settlement batch finishes inside the 2 h window
├── Reliability
│   ├── Fault tolerance
│   │   └── (H,H) Payment provider timeout → order parked, retried, zero lost orders
│   └── Recoverability
│       └── (H,M) Node crash during order intake → resume < 30 s, no duplicates
├── Maintainability
│   └── Modifiability
│       └── (M,H) New payment provider added touching only the payments module, ≤ 3 days
└── Security
    └── Integrity
        └── (H,M) Replayed payment callback rejected and logged, zero successful replays
```

Reading the tree: the two (H,H)/(M,H) leaves — provider-timeout handling and the
payments-module boundary — are where the evaluation digs; the batch window gets a nod; nothing
else consumes session time. The tree also exposes imbalance: a characteristic with no leaves
either doesn't matter (fine, say so) or was forgotten (fix before evaluating). Keep the tree in
the architecture documentation (arc42 §10) — it is the prioritized form of the quality
requirements, not a throwaway workshop artifact.

## ATAM-lite: roles and steps for a half-day

Full ATAM is a multi-day, two-phase workshop with nine steps and external evaluators. Below
organization-critical scale, a half-day session keeps the mechanism that does the work — walking
prioritized scenarios through the architecture in front of people who can contradict the
architect — and drops the ceremony.

Roles (people can double up, with one exception):

| Role | Does | Note |
|---|---|---|
| Evaluation lead | Runs the agenda, keeps analysis on the current scenario, forces risks to be written down | Must not be the design author — the author defending and the lead probing is the engine of the method |
| Architect / design author | Presents the architecture, answers "what happens when…" honestly | Presenting is answering questions, not pitching |
| Scribe | Records risks, non-risks, sensitivity and trade-off points verbatim during discussion | Findings not written in-session are lost |
| Stakeholders (3–5) | Supply scenarios the team didn't think of; sanity-check business value ranks | Ops, security, a downstream consumer team, product |

Steps:

1. Business drivers (15 min) — product side restates goals and constraints; disagreements about
   what matters surface here, not during scenario analysis.
2. Architecture presentation (30 min) — context and container views plus the runtime view of
   the riskiest interaction; the views must pre-exist (see `c4-arc42.md`).
3. Identify approaches (15 min) — name the architectural approaches in play (for example
   "outbox + JetStream for order events", "payments behind a provider-agnostic interface").
   Named approaches are what risks attach to.
4. Utility tree (45 min) — build or review the tree above; stakeholders adjust business-value
   ranks, the architect adjusts risk ranks. Rank disputes are information: record them.
5. Analyze high-priority scenarios (60–90 min) — for each (H,H)/(H,M) leaf, walk the scenario
   through the views: which blocks participate, what happens at each step, where does it
   stress the design. Record per scenario: approaches that address it, risks, non-risks,
   sensitivity points, trade-off points (definitions in `isaqb-concepts.md`).
6. Readout (15 min) — group risks into themes, map each theme back to the business drivers it
   threatens, agree owner and next step per risk (mitigate, spike, accept, or redesign).

Outputs land where they survive: risk list in arc42 §11, decisions triggered by the session as
ADRs (`adr-practice.md`), spikes and mitigations as tasks in the phased plan. An evaluation
whose findings exist only in meeting notes did not happen.

## Fitness-function catalog

A fitness function is an automated, objective check on an architectural characteristic — a
quality scenario's response measure turned into CI. Two classification axes: triggered (runs on
an event: commit, PR, nightly) vs continual (always-on monitoring in or near production), and
atomic (one characteristic, one context) vs holistic (several characteristics interacting).
Derive every fitness function from a quality scenario or an ADR confirmation — a check nobody
can trace to a goal is lint, and dead weight in the pipeline.

| Concern | Check | When | Tooling by ecosystem |
|---|---|---|---|
| Layer / module dependency rules | Forbidden imports (ui must not import persistence; modules only via published API) | Every build, as unit tests | Java: ArchUnit, Spring Modulith verification; .NET: NetArchTest / ArchUnit.NET; TS/JS: dependency-cruiser, eslint boundaries rules; Python: import-linter; Go: depguard |
| Dependency cycles | No cycles between packages/modules | Every build | Same tools; cycle rules are one-liners |
| Naming and placement conventions | Events end in `Event` and live in `events/`; repositories only in `persistence/` | Every build | ArchUnit-family rules; custom lint |
| Contract compatibility | No breaking change against the published API | Every PR | OpenAPI: oasdiff; protobuf/gRPC: buf breaking; Rust: cargo-semver-checks (see `api-design.md`) |
| Performance budget | p95 latency / throughput threshold on the critical endpoint | PR smoke + nightly full run | k6, Gatling, Locust with failure thresholds; Lighthouse CI budgets for web frontends |
| Size and complexity budgets | Module LOC / cyclomatic ceilings; build-time and bundle-size budgets | Every build | Linter thresholds; bundler budget flags |
| Supply chain and security | Dependency audit, secret scan, SBOM diff | Every PR + scheduled | Ecosystem audit tools; see `knowledge/security/supply-chain.md` |
| Reliability in production | SLO burn-rate alerts; synthetic probes of the reliability scenarios | Continual | Monitoring stack; the response measure is the SLO |
| Failure behavior | Consumer crash → redelivery; provider timeout → order parked (scenario R-leaves as integration tests) | Nightly / pre-release | Testcontainers-style integration tests, fault injection |

Working rules:

- Atomic-triggered checks (the first six rows) belong in the same pipeline as unit tests and
  must be fast enough to run on every commit; a fitness function developers routinely skip
  guards nothing.
- Holistic and continual functions (SLO burn, chaos-style fault injection) are owned like
  production code: alert routing, runbooks, review when they fire.
- When an ADR's Confirmation section says "review checks that…", ask whether a fitness function
  can check it instead — mechanical confirmation survives team turnover; review discipline
  doesn't.
- New fitness functions on an existing codebase start in report-only mode with a baseline
  (grandfathered violations listed, new ones blocked), otherwise the gate gets deleted in the
  first sprint.

## As-built vs as-documented drift

Evaluation walks scenarios through the architecture views; if the views don't match the code,
the evaluation validates a fiction. Drift is the normal state — emergency fixes, dependency
creep, undocumented shortcuts — so detection must be routine, not archaeological.

Detection, cheapest first:

1. Encode the documented rules as dependency fitness functions (table above). Every documented
   boundary that exists only in a diagram is unverified; once encoded, drift fails the build
   the day it's introduced instead of surfacing at the next redesign.
2. Generate the dependency graph from code (dependency-cruiser, ArchUnit's slices,
   import-linter's contract report, `go mod graph`) and diff it against the container/component
   diagram at a fixed cadence — quarterly, or before any evaluation session.
3. Keep structural diagrams as code from one model (Mermaid sources in the repo, or a
   Structurizr-style single model rendering all views) so a structural change shows up as a
   reviewable diff next to the code change (`diagramming.md`).
4. Walk the ADR log against reality: for each accepted ADR, is the decision still true in code?
   A violated ADR is either a regression (fix the code) or a silent re-decision (write the
   superseding ADR — `adr-practice.md`).

Triage rule for every drift finding, with no third option: the code is wrong (restore the
documented design, or schedule it) or the documentation is wrong (update it and record why the
design changed). Silently tolerated drift compounds — the next reader can't tell load-bearing
deviations from accidents. Start every evaluation session by stating when drift was last
checked and what was found; unexamined drift is itself a finding for arc42 §11, usually the
first one on the list.

Sources: SEI ATAM technical reports (sei.cmu.edu), iSAQB ARCEVAL module (isaqb.org), *Building
Evolutionary Architectures* (Ford, Parsons, Kua, Sadalage); tool and edition anchors in
`knowledge/shared/versions.md`.
