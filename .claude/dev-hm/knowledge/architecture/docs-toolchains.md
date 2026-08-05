# Docs toolchains: generators, CI quality gates, versioned docs

Docs-as-code means documentation lives in the repo, changes through PRs, and passes automated
checks before publishing — the same discipline as code, because unchecked docs rot faster than
unchecked code. This file covers picking a site generator, wiring the QA pipeline, prose
linting, and versioning published docs. Structure and page types are `diataxis.md`'s job;
diagram authoring is `diagramming.md`'s; tool version anchors live in
`knowledge/shared/versions.md`.

## Choosing a site generator

GitHub rendering alone (no generator) is a legitimate baseline: a well-linked `docs/` tree per
the `diataxis.md` layout, readable in the file view. Add a generator when readers need search,
navigation, or versioned docs. Selection for new sites — active maintenance is a hard
requirement, since the generator sits in the publish path for years:

| Generator | Fits | Trade-offs |
|---|---|---|
| Docusaurus | Product/developer docs needing versioning, search, i18n; teams comfortable in the npm ecosystem | React-based stack and its dependency churn; MDX power invites doc-code complexity |
| MkDocs + Material theme | Existing sites, and teams wanting Markdown-plus-YAML simplicity | Upstream MkDocs is dormant and the Material theme entered maintenance mode (late 2025) with the team building a successor (Zensical); fine to keep operating, but check current state per `knowledge/shared/versions.md` before choosing it for a new site |
| Sphinx | Python projects wanting API reference generated from docstrings | reStructuredText heritage (MyST enables Markdown); steeper theming |
| Jekyll (GitHub Pages default) | Minimal sites published straight from the repo with zero pipeline | Ruby stack; weakest docs-specific features of the group |

Decision heuristics: the generator that renders your existing Markdown unmodified beats a
better one that needs mass rewrites; prefer the generator your org already operates (theme,
plugins, and pipeline knowledge transfer); and record the choice as an ADR (`adr-practice.md`)
— migrating a docs site is a real migration.

## The docs QA pipeline

Run the same checks locally and in CI on every PR touching docs; publish only from green. Order
matters — cheap and deterministic first:

1. Markdown lint — `markdownlint-cli2` (or the repo's configured linter) with a committed
   config; catches structural issues (heading order, bare URLs, malformed lists) that break
   rendering subtly.
2. Prose lint — Vale (below), gating only on error-level rules.
3. Link check — lychee over the built or source tree: internal relative paths, anchors, and
   external URLs. Use its cache and a committed ignore-file for known-flaky external hosts;
   check external links on a schedule (nightly/weekly job) as well, because the web rots
   independently of your commits — a scheduled failure is a docs bug found before a reader
   finds it.
4. Diagram render — every Mermaid block must render (`mmdc` per file, or the generator's
   Mermaid plugin in strict mode); a diagram that doesn't render is a broken build
   (`diagramming.md`).
5. Strict build — the generator with warnings-as-errors (`mkdocs build --strict`; Docusaurus
   with `onBrokenLinks: 'throw'` and broken-anchor checking): unresolved nav entries, missing
   pages, bad cross-references fail here.
6. Preview deploy per PR when the platform supports it — reviewers judge the rendered site,
   not the diff's Markdown.

These checks are the documentation's fitness functions (`architecture-evaluation.md`): each
exists because a class of reader-visible breakage exists, and a check that never fails locally
should still run in CI, where a different OS, clean checkout, and no local cache find the rest.

## Prose linting with Vale

Vale is the de facto standard prose linter for docs-as-code: style rules as data (YAML), run as
CLI locally, in editors, and in CI. Working setup:

- Start from a published style (Google and Microsoft developer style guides ship as Vale
  packages) rather than writing rules from scratch; layer a small org style on top.
- Maintain a project vocabulary (`accept.txt` / `reject.txt`) for product names, domain terms,
  and forbidden aliases — this is where "the product is called X, never Y" becomes enforceable.
- Gate CI on errors only; keep suggestions and warnings as editor feedback. A prose linter that
  blocks PRs on style nits gets disabled within a month — introduce on new/changed files first
  (diff-scoped), not the whole corpus at once.
- Rule of thumb for severity: error = factual/terminology/inclusivity rules; warning =
  style-guide conformance; suggestion = taste.

## Versioned docs

Version published docs only when readers genuinely run different versions of the product
(libraries, APIs, self-hosted software); a SaaS with one live version needs "latest" plus a
changelog, and every extra published version multiplies link-check surface and backport effort.

- Docusaurus: built-in versioning — snapshot on release, `latest` plus supported majors.
- MkDocs: the `mike` tool publishes each version to a subdirectory of the Pages branch with a
  version selector and aliases (`latest`, `stable`).
- Policy before tooling, and keep it aligned with the product's actual support policy: publish
  latest + still-supported majors only; docs for EOL versions redirect to an archive note.
  Fixes land in `latest` and are backported only when the older version is genuinely affected.
- Default version = the one most readers should land on (usually latest stable, not a
  prerelease); make version switching visible so search-engine deep links into old versions
  don't strand readers — a banner on non-latest versions linking to the same page in latest.

## Publishing and restructuring

- Publish via CI (GitHub Actions to GitHub Pages or equivalent), never by hand from a laptop;
  the deploy job runs the full QA pipeline first.
- When restructuring moves or renames pages, preserve inbound links: configure redirects
  (Docusaurus client-redirects; mkdocs-redirects) or leave stub pages, and update referrers in
  the same change (`diataxis.md` on splitting pages). External links and bookmarks can't be
  updated — a moved page without a redirect is a broken contract.
- Keep generated reference (API docs from source) in the same site but out of the hand-written
  tree, regenerated by the pipeline, never hand-edited — one direction of truth
  (`diataxis.md` reference type).

Sources: docusaurus.io, mkdocs.org and squidfunk.github.io/mkdocs-material, vale.sh,
lychee.cli.rs, github.com/DavidAnson/markdownlint-cli2, github.com/jimporter/mike; version and
maintenance-state anchors in `knowledge/shared/versions.md`.
