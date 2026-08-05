# TOGAF ADM applied to product-scale work

The TOGAF Standard (The Open Group; current edition per `knowledge/shared/versions.md`) is
structured as a modular set: the TOGAF Fundamental Content (six documents — Introduction and
Core Concepts, Architecture Development Method, ADM Techniques, Applying the ADM, Architecture
Content, EA Capability and Governance) plus an expanding library of TOGAF Series Guides for
specific contexts (agile enterprises, digital, security, business architecture). The method
itself — the ADM — is stable; the modular restructuring mostly made it easier to adopt
piecemeal. Adopting it piecemeal is exactly what product-scale work should do.

## The ADM cycle

Requirements Management sits at the center of the cycle: every phase can surface new requirements
and consume changed ones. The phases around it:

| Phase | Enterprise meaning | Product/team-scale translation |
|---|---|---|
| Preliminary | Establish the EA capability, principles, tailoring | Agree how architecture work happens in this team: who decides, where ADRs live, what "done" means for a design |
| A. Architecture Vision | Scope the engagement, stakeholders, high-level target | One-page vision: problem, quality goals, rough target sketch (context diagram), success criteria |
| B. Business Architecture | Business strategy, capabilities, processes | Understand the domain flows the change touches; name the capabilities affected |
| C. Information Systems Architectures | Data + application architecture | Data model and service/module design for the change |
| D. Technology Architecture | Platform, infrastructure | Runtime, deployment, and infrastructure choices |
| E. Opportunities & Solutions | Consolidate gaps, identify work packages | Gap list between baseline and target; group gaps into implementable increments |
| F. Migration Planning | Roadmap, transition architectures | The phased implementation plan with dependencies and parallel groups |
| G. Implementation Governance | Ensure conformance during build | Reviews and gates verify the build matches the ADRs and target views |
| H. Architecture Change Management | Keep architecture current | Re-open the design when reality diverges; supersede ADRs instead of ignoring them |

Two ideas from the ADM survive any amount of scaling down and are worth keeping even for a
two-week feature:

- Baseline vs target, then gap. Describe what exists, describe what should exist, and derive
  work only from the difference. Plans invented without a baseline systematically miss
  migration and coexistence work.
- Transition architectures. When the target can't be reached in one step, define intermediate
  states that are each shippable and stable. Phases in an implementation plan should be
  transition architectures, not arbitrary task groupings.

## The compressed cycle for solution-scale work

Below enterprise-landscape scale, run the ADM as four moves instead of ten phases:

1. Vision (A): problem statement, stakeholders, top quality goals as scenarios, context diagram.
2. Target (B–D collapsed): the target architecture across business/data/application/technology
   concerns at once — a container diagram, key runtime scenarios, and technology choices with
   trade-offs. Split into separate B/C/D passes only when different specialist groups own the
   domains.
3. Gap and increments (E): what changes between baseline and target, grouped into increments
   that each leave the system consistent.
4. Migration plan (F): the phased plan with dependencies, parallel groups, and acceptance
   criteria per task.

Governance (G) collapses into the existing review/gate process; change management (H) collapses
into ADR lifecycle discipline (supersede, don't silently drift).

## Architecture principles as team guardrails

The principles technique from the ADM Techniques document survives scaling down well: a
principle is a durable rule that pre-decides a whole class of choices, so each instance doesn't
need its own debate. State each with four parts:

| Part | Content |
|---|---|
| Name | Memorable — "Buy before build", "Every service owns its data" |
| Statement | The rule, one sentence |
| Rationale | Why it serves the goals — tie it to quality goals where possible |
| Implications | What following it costs and requires |

Keep the set small (five to ten); principles that contradict each other or the quality goals get
resolved, not accumulated. Enforce through review plus recorded exceptions — an exception granted
twice is a signal to revise the principle. Principles differ from ADRs: a principle guides many
future decisions, an ADR records one decision already taken. When a decision violates a
principle, its ADR names the principle and justifies the exception.

## When full TOGAF is overkill — and when it isn't

Say so explicitly in architecture output when the full method would be ceremony. Full TOGAF
ceremony (separate phase iterations, formal architecture boards, statement-of-architecture-work
contracts, capability assessments) earns its cost when several of these hold:

- The change spans multiple systems owned by different teams or organizations.
- There is a standing EA function whose repository and principles the work must land in.
- Regulatory or contractual obligations require formal architecture governance artifacts.
- The landscape itself (not one product) is being reshaped — mergers, platform consolidation.

For one product, one team, or one service, the compressed cycle plus ADRs delivers the same
decisions at a fraction of the artifact count. The failure mode to avoid isn't "too little
TOGAF"; it's skipping the baseline/gap/transition thinking and jumping from idea to task list.

## Enterprise Continuum and Architecture Repository, scaled down

The Enterprise Continuum is TOGAF's classification of reusable assets along a
generic-to-specific axis, in two strands: the Architecture Continuum (reusable architecture
building blocks, from foundation architectures through industry to organization-specific) and
the Solutions Continuum (the concrete implementations of those blocks). The practical takeaway
at team scale: before designing, look for existing assets to specialize — platform reference
architectures, an in-house service template, a prior ADR from a sibling team — and record in the
ADR which asset was reused or why none fit.

The Architecture Repository is where an EA function stores its assets. Its main compartments and
their team-scale equivalents:

| Repository compartment | Holds | Team-scale equivalent |
|---|---|---|
| Architecture Metamodel | How architecture content is structured | The doc conventions: arc42 sections used, C4 levels drawn, ADR template |
| Architecture Landscape | Baseline/target/transition architectures | `docs/architecture/` views of current and target state |
| Reference Library | Reusable patterns, templates | Shared knowledge files, service templates, platform docs |
| Standards Information Base | Standards the work must comply with | Pinned versions, security baselines, org-wide constraints |
| Governance Log | Decisions and compliance records | The ADR directory and review/gate verdicts |
| Architecture Capability | Skills, roles, charters | Who owns architecture decisions in the team |

A repo with `docs/architecture/` (views), `docs/decisions/` (ADRs), and a linked platform
reference already implements the useful core of the repository idea.

## Mapping ADM outputs to this plugin's artifacts

| ADM output | Concrete artifact here |
|---|---|
| Architecture Vision | Vision section of the architecture package: problem, quality scenarios, context diagram |
| Target architecture (B–D) | arc42 §4–§7 content: solution strategy, building block, runtime, deployment views |
| Architecture Requirements | Quality scenarios (see `isaqb-concepts.md`) and constraints (arc42 §2) |
| Gap analysis / work packages (E) | Increment list feeding the phased plan |
| Migration Plan (F) | Phased implementation plan with parallel groups and acceptance criteria |
| Architecture Contract (G) | Work packages referencing the ADRs and views they must respect |
| Change requests (H) | New or superseding ADRs |

Sources: The Open Group, TOGAF Standard (opengroup.org/togaf); edition anchor in
`knowledge/shared/versions.md`.
