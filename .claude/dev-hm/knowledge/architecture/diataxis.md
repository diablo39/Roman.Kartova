# Diátaxis: documentation types and repo layout

Diátaxis (diataxis.fr, Daniele Procida) classifies documentation by the reader's situation along
two axes: is the reader acquiring skill or applying it, and do they need practical steps or
theoretical understanding? That yields four types, each with its own form, tone, and failure
modes. Most bad documentation is good content in the wrong type — a tutorial that stops to
explain theory, a reference that tries to teach.

## The four types

| | Serves acquisition (study) | Serves application (work) |
|---|---|---|
| Practical steps | Tutorial | How-to guide |
| Theoretical knowledge | Explanation | Reference |

| Type | Reader's situation | Form | Tone | Success looks like |
|---|---|---|---|---|
| Tutorial | New, needs a guided first experience | A lesson: one path, concrete steps, visible results early and often | Teacher: "we", reassuring, no choices offered | Reader finishes and wants more |
| How-to guide | Competent, has a task | A recipe: goal in the title, numbered steps, prerequisites stated | Colleague: direct imperatives, assumes competence | Task done, minimal reading |
| Reference | Working, needs facts | Structured description: tables, signatures, parameters, defaults | Neutral, austere, complete and consistent | Fact found fast, trusted |
| Explanation | Curious, wants understanding | Discussion: context, alternatives, why it is this way | Considered, admits trade-offs | Reader's mental model improved |

Type-specific anti-patterns:

- Tutorial that explains everything it touches → learners drown; link to explanation instead.
- Tutorial with untested steps → one broken step ends the learner's trust; run every step.
- How-to that teaches concepts mid-task → the worker came to finish, not to study.
- Reference with opinions and narrative → readers can't scan; move judgment to explanation.
- Reference that's incomplete → readers stop trusting all of it; completeness is the feature.
- Explanation full of step-by-step instructions → it's a how-to wearing a costume; split it.

## The compass

When a page's type is unclear, two questions resolve most classification arguments faster than
debating the quadrant chart:

1. Does the content inform action (the reader does something) or cognition (the reader
   understands something)?
2. Does it serve the acquisition of skill (study) or the application of skill (work)?

| Informs | Serves | Write a |
|---|---|---|
| action | acquisition | tutorial |
| action | application | how-to guide |
| cognition | application | reference |
| cognition | acquisition | explanation |

Apply the compass to each section of a page, not just its title — mixed pages reveal themselves
as sections whose answers differ.

## Classifying and fixing existing pages

For each page ask: who arrives here, and what do they want to leave with? Then check whether the
form matches the type. Common findings in real repos:

- The overloaded README: quick-start (tutorial), install matrix (reference), design rationale
  (explanation) in one file. Keep the README as a short map — what the project is, one
  quick-start pointer, links per doc type — and move the rest out.
- The "guide" that alternates steps and theory: split into a how-to and an explanation that
  link to each other.
- API docs with usage advice interleaved: keep generated/structured reference pure; collect
  advice into how-tos ("How to paginate results").

When splitting, preserve inbound links: leave a stub with a redirect link, or update referrers
in the same change.

## Repo layout

A structure that maps types to directories and works on GitHub and GitHub Pages:

```
docs/
├── index.md                  # map of the docs: what's here, per type
├── tutorials/
│   ├── index.md              # ordered list, "start here"
│   └── getting-started.md
├── how-to/
│   ├── index.md              # task-titled list ("Add a provider", "Rotate keys")
│   └── add-a-provider.md
├── reference/
│   ├── index.md
│   ├── configuration.md      # tables: option, type, default, effect
│   └── api/                  # generated reference lands here
├── explanation/
│   ├── index.md
│   └── why-event-sourcing.md
├── architecture/             # arc42-shaped views (see c4-arc42.md)
└── decisions/                # ADRs (see adr-practice.md)
```

Conventions:

- Every directory has an `index.md` naming its contents; every page links back to its index.
- File names are lowercase-with-dashes and say what the page is for: how-tos titled as tasks
  ("deploy-to-staging.md"), references titled as nouns ("configuration.md").
- Use relative links (`../reference/configuration.md`) so navigation works in the GitHub file
  view and on a Pages site alike. For Pages generators that rewrite URLs (MkDocs, Docusaurus),
  follow the generator's link convention and verify with a local build.
- Architecture documentation is its own cluster: arc42-shaped content under
  `docs/architecture/`, decision records under `docs/decisions/`. In Diátaxis terms these are
  mostly explanation and reference, but keeping them in the standard locations beats filing
  them by type — findability wins.

Scale the skeleton to the project. A small library may need one file per type; don't create
empty directories to satisfy the diagram.

## Cross-referencing between types

The types form a system; links carry readers between them at the natural moments:

| From | Link to | At the point where |
|---|---|---|
| Tutorial | Explanation | A curious learner would ask "why?" — link, don't digress |
| Tutorial | How-to guides | The lesson ends: "now you can: [task], [task]" |
| How-to | Reference | A step uses an option or API worth looking up |
| How-to | Explanation | A step makes a choice whose rationale exists elsewhere |
| Reference | How-to guides | An entry is commonly used in a known task |
| Explanation | Everything relevant | Grounding claims in concrete material |

Avoid duplicating content across types — duplicate copies drift, and drift is worse than a
click. One fact, one home, many links.

## Choosing what to write first

When documenting an undocumented system, the highest-value order is usually: one honest
quick-start tutorial (proves the setup path works), how-tos for the five most common tasks
(mined from support questions and onboarding notes), reference for whatever people currently
read source code to learn, and explanation only where decisions keep getting relitigated —
though at that point an ADR (see `adr-practice.md`) may be the better vehicle.

Source: diataxis.fr (Daniele Procida; CC-BY-SA); currency anchor in
`knowledge/shared/versions.md` (unversioned living framework).
