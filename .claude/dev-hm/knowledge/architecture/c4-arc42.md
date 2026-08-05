# C4 and arc42: viewpoints and templates

Two complementary tools: the C4 model (c4model.com, Simon Brown) defines the abstraction levels
for structural diagrams; arc42 (arc42.org; version per `knowledge/shared/versions.md`) defines
the table of contents for a full
architecture document. Use C4 to decide what a diagram shows, arc42 to decide where prose and
diagrams live. Both are free to use; neither prescribes tooling. For Mermaid syntax to render
these views, see `diagramming.md`.

## C4: four levels plus three supplements

C4 is a hierarchy of abstractions — software systems, containers, components, code — with one
diagram type per level. Each level zooms into one element of the level above. The model is
notation-independent; what matters is that every element carries a name, a type, and a
responsibility-revealing description, and every relationship a label.

| Level | Diagram | Shows | Audience | Draw it when |
|---|---|---|---|---|
| 1 | System context | The system as one box, its users and neighboring systems | Everyone, including non-technical | Almost always — it's the cheapest, most durable diagram |
| 2 | Container | Deployable/runnable units (services, SPAs, databases, brokers) and their interactions | Technical people in and around the team | Almost always — the workhorse view |
| 3 | Component | Major structural parts inside one container and their responsibilities | Developers of that container | Only for containers with non-obvious internals worth explaining |
| 4 | Code | Classes/functions | Developers | Rarely — IDEs generate this on demand; hand-drawn versions rot fastest |

Supplementary diagram types:

| Diagram | Shows | Use for |
|---|---|---|
| System landscape | Multiple systems and their users across an organization | Enterprise context around your system |
| Dynamic | Elements collaborating for one scenario, numbered steps | The riskiest or least obvious runtime interactions |
| Deployment | Mapping of containers to infrastructure (nodes, regions, orchestrators) | Anything where infrastructure choices matter |

Working rules:

- Titles state level and scope: "Container diagram — Order System".
- A diagram needs a legend unless the notation is genuinely self-evident.
- Aim for fewer than ~20 boxes per diagram; past that, split by zooming.
- Don't produce all levels reflexively. Context + container covers most solution work;
  add dynamic diagrams for scenarios reviewers question, deployment when infra changes.

## arc42: the twelve sections

arc42 is a template, not a process: twelve sections, every one optional, filled in any order.
Its two governing rules: document only what a reader needs, and put content where the template
says it belongs so readers can find it.

| § | Section | Content | Fill it when |
|---|---|---|---|
| 1 | Introduction and Goals | Requirements overview, top 3–5 quality goals, stakeholders | Always — quality goals drive everything else |
| 2 | Architecture Constraints | Technical, organizational, regulatory constraints | There are real constraints (there always are) |
| 3 | System Scope and Context | Business and technical context, external interfaces | Always — this is the C4 context diagram plus interface tables |
| 4 | Solution Strategy | Fundamental decisions and approaches, in brief | Always — half a page linking goals to approaches |
| 5 | Building Block View | Static decomposition, hierarchical black/white boxes | Always at level 1 (= C4 container); deeper levels only where needed |
| 6 | Runtime View | Key scenarios as interactions | The few scenarios that are risky or non-obvious |
| 7 | Deployment View | Infrastructure and mapping of software to it | Deployment is non-trivial or changing |
| 8 | Cross-cutting Concepts | Recurring solutions: persistence, security, error handling, logging | A concept spans building blocks and would otherwise be documented n times |
| 9 | Architecture Decisions | Important decisions with rationale | Always — as links to ADR files, not restated prose (see `adr-practice.md`) |
| 10 | Quality Requirements | Quality tree and concrete scenarios | Always — the scenarios from `isaqb-concepts.md` live here (top goals summarized in §1.2) |
| 11 | Risks and Technical Debts | Known problems, ordered by priority | Evaluation found risks, or debt was accepted deliberately |
| 12 | Glossary | Domain and technical terms | Terms are ambiguous or translated |

A pragmatic minimum for a solution design document: §1, §3, §4, §5 (one level), §9, §10 —
roughly six pages — growing §6/§7/§8/§11 as the work reveals the need.

## Mapping C4 into arc42

The two fit together with no friction:

| arc42 section | C4 content |
|---|---|
| §3 System Scope and Context | System context diagram (and system landscape, if drawn) |
| §5 Building Block View, level 1 | Container diagram |
| §5 Building Block View, level 2+ | Component diagrams per container |
| §6 Runtime View | Dynamic diagrams |
| §7 Deployment View | Deployment diagrams |

## Choosing what to produce

Start from stakeholder questions, not from the template:

| Question being asked | Produce |
|---|---|
| "What is this system and what does it talk to?" | §3 + context diagram |
| "How is it structured / where does my change go?" | §5 + container diagram (+ component where needed) |
| "How does scenario X actually work?" | §6 + dynamic diagram for X |
| "Why is it built this way?" | §4 + ADRs linked from §9 |
| "Will it meet quality goal Y?" | §10 scenarios + evaluation notes in §11 |
| "Where does it run / how do we deploy?" | §7 + deployment diagram |

Documentation that answers no current stakeholder question is inventory, not communication —
defer it. The reverse also holds: a recurring question with no stable document answering it is
a documentation gap worth closing.

Sources: c4model.com (canonical, unversioned living model), arc42.org; version anchors in
`knowledge/shared/versions.md`.
