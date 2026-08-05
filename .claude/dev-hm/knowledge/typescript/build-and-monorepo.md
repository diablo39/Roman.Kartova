# Build and monorepo: pnpm workspaces, project references, packaging

Repository and build mechanics for TypeScript: workspace layout, incremental type-checking,
bundling, and publishing libraries that resolve correctly for every consumer. Tool generations are
pinned in `knowledge/shared/versions.md`; bundle analysis and slow-`tsc` diagnosis are in
`debugging.md`. Consolidating repos into a monorepo (or splitting one) is a consequential
structural decision — analyze it per `knowledge/shared/three-framing-analysis.md` first.

## pnpm workspaces

pnpm is the default package manager for workspaces: content-addressed storage, strict
`node_modules` (no phantom dependencies — a package can only import what it declares), and
first-class workspace linking.

```yaml
# pnpm-workspace.yaml
packages:
  - "apps/*"
  - "packages/*"
```

- Internal dependencies use the workspace protocol: `"@acme/core": "workspace:^"`. Local resolution
  during development; on `pnpm publish` the protocol is replaced with the real version range.
- Catalogs define a shared dependency range once in `pnpm-workspace.yaml` (`catalog:` section);
  packages reference it with `"zod": "catalog:"`. One place to bump, no cross-package version
  drift — treat the catalog as the workspace's version policy for shared deps.
- Targeted runs: `pnpm --filter @acme/api test` (one package), `--filter ...@acme/core` (a package
  and everything depending on it), `-r` (all, topologically ordered).
- CI installs with `pnpm install --frozen-lockfile` (`security.md#deps`).

## TypeScript project references

Project references give the workspace an enforced dependency graph and incremental, cacheable
type-checking — each package compiles against its dependencies' declaration output, not their
sources.

```jsonc
// packages/api/tsconfig.json
{
  "extends": "../../tsconfig.base.json",     // strict family lives once, in the base
  "compilerOptions": { "composite": true, "outDir": "dist", "rootDir": "src" },
  "include": ["src"],
  "references": [{ "path": "../core" }]
}
// tsconfig.json (repo root): references every package; no files of its own
```

- Build/check with `tsc -b` (add `--watch` locally). The native-compiler generation
  (`knowledge/shared/versions.md`, TypeScript row) builds the same reference graph, dramatically
  faster — same config, no migration.
- The graph is enforced: importing a package not listed in `references` fails, which keeps module
  boundaries honest (the review checklist's deep-import finding).
- Module settings differ by artifact: published Node libraries use `module: "nodenext"`; bundled
  apps use `module: "preserve"` with bundler resolution (`platform.md`). Keep both variants in
  shared base configs rather than hand-tuning per package.
- Type-check in CI separately from bundling — bundlers do not type-check (`platform.md`).

## Task orchestration

Start with what the tools already provide: `pnpm -r --filter` for fan-out and `tsc -b` for
incremental type builds. Adopt a task orchestrator (Turborepo, Nx) when the repo outgrows that —
the signals are CI minutes dominated by rebuilding unaffected packages, or a need for remote/shared
caching across developers and CI. The orchestrator wraps the same package scripts; it does not
replace project references, which remain the type-level cache boundary.

## Application builds: Vite

Vite is the app build (`platform.md`); the current generation runs on the Rust-based Rolldown
bundler (`knowledge/shared/versions.md`, TS build/test row) with the same config surface.

- Code splitting: each dynamic `import()` becomes a chunk. Split at route boundaries first
  (`React.lazy` + `Suspense` — UX and budgets in `performance.md`); split below that only after
  measuring.
- Chunking strategy (e.g. a vendor chunk via the bundler's `advancedChunks`/manual-chunks config)
  is an optimization to apply after reading the bundle analysis (`debugging.md`), not a default.
- Environment variables in client builds follow the public-prefix rule (`security.md#secrets`).

## Packaging a library

ESM-only is the default for a new library: one artifact, no dual-package hazard, and every current
runtime and bundler consumes it. Publish dual ESM+CJS only when a concrete consumer that cannot
load ESM exists today (a checkable fact, not a hedge) — and then let the bundler emit both from one
source, never hand-maintained parallel trees.

```jsonc
// package.json of a published library
{
  "name": "@acme/dates",
  "type": "module",
  "exports": {
    ".":    { "types": "./dist/index.d.ts", "default": "./dist/index.js" },
    "./tz": { "types": "./dist/tz.d.ts",    "default": "./dist/tz.js" },
    "./package.json": "./package.json"
  },
  "files": ["dist"],
  "sideEffects": false
}
```

- The `exports` map is the public API: consumers can import exactly these subpaths and nothing
  else. In each condition object, `"types"` comes first; `"default"` last.
- `files` allowlists what ships; `sideEffects: false` unlocks tree-shaking (list exceptions like
  CSS imports explicitly if any).
- Build with a library bundler — `tsdown` (Rolldown-based, the successor in the tsup lineage) or
  Vite library mode — emitting JS, `.d.ts`, source maps, and declaration maps so consumers'
  go-to-definition lands in your source.
- Do not point `exports` at TypeScript source, and do not rely on legacy top-level
  `main`/`module`/`types` fields for new packages — `exports` governs resolution.

### Verify the package before it ships

Two classes of packaging bugs are invisible in the repo and break only for consumers: `exports`
maps that don't match the emitted files, and type declarations that resolve differently per module
mode. Gate both in CI on every publishable package:

```bash
npm pack                          # build the real tarball
npx publint                       # package.json fields vs actual artifacts
npx @arethetypeswrong/cli --pack  # type resolution across consumer module modes
```

`tsdown` can run publint as part of the build. Publish from CI with provenance
(`npm publish --provenance`) and a frozen lockfile; multi-package version/changelog management is
what changesets exists for.

## Workspace hygiene

- One tool per job across the workspace: one package manager, one test runner, one formatter — per-
  package divergence multiplies maintenance without benefit.
- Shared configs (`tsconfig.base.json`, ESLint flat config, Prettier) live in a root or a dedicated
  `packages/config` package; leaf packages extend rather than copy.
- An internal package that will never publish still gets an `exports` map — it documents the public
  surface and keeps sibling packages out of its internals.
- Lockfile is singular and committed; renovate-style dependency updates land as ordinary reviewed
  PRs subject to the same audit gates (`security.md#deps`).
