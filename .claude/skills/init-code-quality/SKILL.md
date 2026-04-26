---
name: init-code-quality
description: "Initialize .NET mutation testing and coverage setup. Use when: checking whether Stryker.NET is installed, adding a default stryker-config.json, verifying coverlet collector configuration, adding a default coverlet.runsettings, or onboarding a repo for code-quality tooling."
argument-hint: "Optional: provide a solution path or test project path to scope the bootstrap"
---

# Init Code Quality

You are the bootstrap step for .NET test-quality tooling. Your job is to inspect the repository for mutation-testing and coverage prerequisites, add the missing pieces with conservative defaults, and leave the repo in a state where the user can refine the generated templates.

## When to Use

- When a .NET repository does not yet have Stryker.NET mutation testing wired in
- When a test project is missing `coverlet.collector` or a shared runsettings file
- When the user wants a safe first-pass `stryker-config.json` template to customize later
- When the user wants a safe first-pass `coverlet.runsettings` template to customize later
- Before running mutation or coverage skills that assume baseline tooling already exists

## Inputs and Outputs

| Type | Path | Purpose |
|---|---|---|
| Skill instructions | This file | Decision rules and bootstrap procedure |
| Stryker template | `./assets/stryker-config.template.json` | Default config to copy into the repo when no Stryker config exists |
| Mutation targets template | `./assets/mutation-targets.template.json` | Default config to copy into the repo when no mutation-targets.json exists |
| Coverlet template | `./assets/coverlet.runsettings.template.xml` | Default runsettings to copy into the repo when no Coverlet config exists |

Primary output:

- installed or verified `dotnet-stryker`
- installed or verified `coverlet.collector` on the chosen test project(s)
- added or verified `stryker-config.json`
- added or verified `mutation-targets.json` with auto-discovered source projects
- added or verified `coverlet.runsettings`
- concise summary of what already existed, what was added, and what still needs user alignment

## Non-Negotiable Rules

You MUST:

- Restrict this workflow to .NET repositories and stop if no solution or test project can be identified
- Check for NuGet authentication problems before any `dotnet restore`, `dotnet build`, or `dotnet test` call when the repo uses private feeds
- Prefer a local tool manifest for `dotnet-stryker` so the repo is self-contained
- Prefer `coverlet.collector` plus a shared `.runsettings` file as the default coverage setup
- Reuse existing configuration when present instead of replacing it with the templates
- Discover source projects from test project `ProjectReference` entries for `mutation-targets.json`
- Copy the bundled templates only when the relevant config file does not already exist
- Summarize exact file paths and package or tool changes at the end

You MUST NOT:

- Overwrite an existing `stryker-config.json`, `.runsettings`, or test project package reference without explicit user approval
- Invent repository-specific exclusions or mutate globs without checking the actual code layout
- Claim the repo is ready for mutation testing if restore, build, or baseline tests are failing
- Add both `coverlet.collector` and `coverlet.msbuild` unless the repo already depends on that combination

## Detection Rules

### Step 1: Confirm the repository can be bootstrapped

Look for one of these anchors first:

- a `.sln` or `.slnx` file
- at least one `*.Tests.csproj` or `*.Test.csproj`
- an explicit solution path or test project path from the user

If none exist, stop and report that the skill only supports repositories with a detectable .NET test surface.

### Step 2: Choose the test project scope

Use this order:

1. If the user supplied a test project path, use it.
2. If exactly one likely unit test project exists, use it.
3. If multiple candidates exist, prefer unit-test projects over integration or functional test projects.
4. If multiple equally plausible candidates remain, ask the user which project(s) to bootstrap before making package changes.

### Step 3: Detect Stryker.NET installation and config

Check in this order:

1. `.config/dotnet-tools.json` for `dotnet-stryker`
2. global tools for `dotnet-stryker`
3. repo config files named `stryker-config.json`

Interpretation:

- local tool manifest entry present: installation is good
- only global tool present: usable, but add a local tool manifest unless the user explicitly wants a machine-level install only
- no tool present: create a local tool manifest and install `dotnet-stryker`
- any existing `stryker-config.json`: preserve it and do not replace it
- no config file: add a new `stryker-config.json` from [stryker-config.template.json](./assets/stryker-config.template.json)

### Step 4: Detect Coverlet installation and config

For each chosen test project, inspect `PackageReference` entries.

Interpretation:

- `coverlet.collector` present: package setup is good
- `coverlet.msbuild` present without `coverlet.collector`: preserve it, but note that this skill defaults to collector-based config for new setups
- neither present: add `coverlet.collector`

Then look for config files in this order:

1. `coverlet.runsettings`
2. `*.runsettings`
3. `test.runsettings`

If an existing runsettings file already configures `XPlat Code Coverage`, preserve it.

If no suitable runsettings file exists, add `coverlet.runsettings` from [coverlet.runsettings.template.xml](./assets/coverlet.runsettings.template.xml).

## Procedure

### Phase 1: Inspect before changing anything

1. Detect the solution and candidate test projects.
2. Check whether the repo contains `nuget.config`, `NuGet.Config`, or `NuGet.config` with private feeds.
3. If private feeds are present, verify authentication before restore or package installation. If authentication fails, stop and ask the user how they authenticate.
4. Detect existing Stryker and Coverlet assets using the rules above.

### Phase 2: Bootstrap missing Stryker pieces

If `dotnet-stryker` is missing locally:

1. Create a local tool manifest if `.config/dotnet-tools.json` does not exist.
2. Install `dotnet-stryker` into the local manifest.

If `stryker-config.json` is missing:

1. Copy [stryker-config.template.json](./assets/stryker-config.template.json) to `stryker-config.json`.
2. Place it at the repo root unless the user explicitly wants per-project config.
3. Do not fill in repo-specific mutate globs beyond the safe defaults already in the template.
4. Populate the `solution` field with the relative path to the solution file (e.g., `"Grid.sln"`).
5. Populate the `test-projects` array with all discovered unit test projects (see below).

If `stryker-config.json` already exists but has an empty `test-projects` array, offer to populate it.

#### Populating `test-projects`

Discover all test projects and add their paths (relative to the repo root) to the `test-projects` array. This tells Stryker which test projects to use, removing the need for external scripts to make project-specific selection decisions.

Include test projects matching these patterns:
- `*Tests.csproj`
- `*Tests.*.csproj` (e.g., `Company.Tests.Services.csproj`)
- `*Test.csproj`

Exclude test projects that are integration or app-host test projects:
- Projects whose name contains `Integration`, `Load`, `Benchmark`, or `Perf`
- Projects that reference `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory-based hosts) — these typically reference the main application project which may contain source generators incompatible with mutation tools

Format the array with paths relative to the repo root:
```json
"test-projects": [
  "./src/Tests/MyProject.Tests.Unit/MyProject.Tests.Unit.csproj",
  "./src/Tests/MyProject.Tests.Data/MyProject.Tests.Data.csproj"
]
```

### Phase 3: Bootstrap mutation-targets.json

The `mutation-targets.json` file is the config contract for `mutation-sentinel-gh` and CI/CD mutation pipelines. It declares which source projects to mutate, organized by stack group.

If `mutation-targets.json` already exists at the repo root, preserve it and do not replace it.

If `mutation-targets.json` is missing:

1. Copy [mutation-targets.template.json](./assets/mutation-targets.template.json) to `mutation-targets.json` at the repo root.
2. Set the `solution` field to the relative path of the solution file discovered in Step 1 (e.g., `"Grid.sln"`).
3. Set the `configFile` field to `"stryker-config.json"` (the Stryker config created or verified in Phase 2).
4. Populate the `projects` array with discovered source projects (see below).

If `mutation-targets.json` already exists but has an empty `projects` array, offer to populate it.

#### Discovering source projects

Extract source projects from the `ProjectReference` entries in each discovered test project:

1. For each test project discovered in Step 2, read its `.csproj` file.
2. Extract all `<ProjectReference Include="...">` entries.
3. Resolve each reference path relative to the repo root.
4. Filter out projects whose name contains `Test`, `Benchmark`, `Load`, or `Perf` (case-insensitive) — these are test or performance projects, not mutation targets.
5. Deduplicate by project filename across all test projects.
6. For each surviving source project, add an entry to the `projects` array:

```json
{ "path": "src/Services/Grid.Proxy/Grid.Proxy.csproj" }
```

All paths must be relative to the repo root, using forward slashes.

#### When names collide

If two source projects have the same filename but different paths (e.g., `src/v1/Core.csproj` and `src/v2/Core.csproj`), add an explicit `name` field to disambiguate:

```json
{ "name": "Core-v1", "path": "src/v1/Core/Core.csproj" },
{ "name": "Core-v2", "path": "src/v2/Core/Core.csproj" }
```

#### Example output

```json
{
  "groups": [
    {
      "stack": ".NET",
      "solution": "Grid.sln",
      "configFile": "stryker-config.json",
      "projects": [
        { "path": "src/Services/Grid.Admin/Grid.Admin.csproj" },
        { "path": "src/Services/Grid.Configuration/Grid.Configuration.csproj" },
        { "path": "src/Database/Grid.ArcGIS/Grid.ArcGIS.csproj" },
        { "path": "src/Shared/Grid.Dotnet/Grid.Dotnet.csproj" }
      ]
    }
  ]
}
```

### Phase 4: Bootstrap missing Coverlet pieces

For each chosen test project missing `coverlet.collector`:

1. Add the package reference.
2. Avoid touching unrelated package versions.

If no suitable runsettings file exists:

1. Copy [coverlet.runsettings.template.xml](./assets/coverlet.runsettings.template.xml) to `coverlet.runsettings` at the repo root.
2. Keep the template generic so the user can tighten exclusions later.

### Phase 5: Validate the bootstrap narrowly

After the first substantive change, the next step must be a focused validation.

Use the narrowest check available:

1. `dotnet tool list --local` to confirm `dotnet-stryker`
2. inspect the changed test project file to confirm `coverlet.collector`
3. run a narrow `dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage" --no-restore` only if restore already succeeded and the repo is authenticated

If validation fails because restore or tests are unhealthy, stop and report that as the blocking condition instead of forcing more config edits.

## Completion Criteria

Do not finish until all applicable checks pass:

- the target .NET solution or test project scope is identified
- `dotnet-stryker` is either locally installed or an explicit user decision was recorded to keep a global-only install
- every chosen test project either already had or now has `coverlet.collector`
- a `stryker-config.json` exists or an existing config was explicitly preserved
- the `test-projects` array in `stryker-config.json` is populated with discovered unit test projects
- a `mutation-targets.json` exists or an existing config was explicitly preserved
- the `projects` array in `mutation-targets.json` is populated with discovered source projects
- a runsettings file for Coverlet exists or an existing one was explicitly preserved
- the summary clearly distinguishes `already present`, `added`, and `blocked`

## Summary Format

End with a concise summary containing:

- solution or test project scope used
- whether `dotnet-stryker` was already present, added locally, or left global-only
- which test projects already had `coverlet.collector` and which were updated
- whether `stryker-config.json`, `mutation-targets.json`, and `coverlet.runsettings` were preserved or created
- which test projects were added to `test-projects` and which were excluded (with reason)
- which source projects were added to `mutation-targets.json` `projects` array and which were excluded (with reason)
- any repo-specific follow-up the user should align manually

## Notes for Ambiguity

Ask the user before changing scope when:

- multiple test projects are equally plausible bootstrap targets
- the repo already uses `coverlet.msbuild` and it is unclear whether collector-based config should be introduced
- the repo already has a `*.runsettings` file that does not mention coverage, but may be intentionally reserved for another test workflow
