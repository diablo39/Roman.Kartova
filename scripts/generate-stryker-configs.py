#!/usr/bin/env python3
"""
Generate per-module Stryker.NET configs from a single template + routing manifest.

Per-module configs route mutation testing to the test projects most likely to
exercise each mutated source project (instead of running every test project
against every mutant). The routing rationale lives in this file's ROUTES dict
so the routing decision is reviewable in code review.

Pairs with `mutation-targets.json` — every project entry there should set
`configFile` to one of the paths emitted here.

Usage
-----
    python scripts/generate-stryker-configs.py             # emit/overwrite configs
    python scripts/generate-stryker-configs.py --validate  # exit 1 if any
                                                            # on-disk config
                                                            # differs from
                                                            # would-be-generated

Wire `--validate` into CI or a pre-commit hook to catch drift between the
manifest and the on-disk configs.

Skipped projects
----------------
- `Kartova.SharedKernel.Wolverine` — no production code currently publishes
  Wolverine messages, so its middleware is structurally unreachable from any
  test. Re-add to ROUTES + `mutation-targets.json` once a slice introduces
  tenant-scoped messaging.

Root `stryker-config.json` (kept)
---------------------------------
Used by direct `dotnet stryker` invocations (no manifest, no module routing).
Lists only the unit test projects so manual local runs stay fast. The
mutation-sentinel skill goes through `mutation-targets.json` and never
references the root config.
"""
from __future__ import annotations

import argparse
import copy
import json
import pathlib
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent

# Common settings shared by every emitted config. Only `test-projects` varies
# per route. Keep this in sync with the root `stryker-config.json` deltas you
# want to share across all per-module configs (mutate excludes,
# ignore-mutations, ignore-methods, thresholds, reporters).
BASE: dict = {
    "stryker-config": {
        "solution": "Kartova.slnx",
        "test-projects": [],  # filled per route below
        "reporters": ["html", "json", "cleartext"],
        "thresholds": {"high": 80, "low": 60, "break": 0},
        "ignore-mutations": ["Regex", "Update", "String", "Linq", "Block"],
        "mutate": [
            "!**/obj/**",
            "!**/bin/**",
            "!**/*.g.cs",
            "!**/*.designer.cs",
            "!**/Program.cs",
            "!**/*.Tests.cs",
            "!**/*.migrations.cs",
            "!**/*.generated.cs",
            "!**/*Designer.cs",
            "!**/Migrations/**",
            "!**/Connected Services/**",
        ],
        "ignore-methods": [
            "*ConfigureAwait*",
            "*Log",
            "Console.Write*",
            "*Exception.ctor",
        ],
    }
}

# Routing manifest: target stryker config path -> list of test-project paths.
# Test-project paths are written exactly as Stryker expects them (relative,
# leading `./`).
ROUTES: dict[str, list[str]] = {
    # Web host: every endpoint runs through Kartova.Api, exercised by all
    # three integration test projects (Keycloak end-to-end + module-level).
    "src/Kartova.Api/stryker-config.json": [
        "./tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj",
        "./src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj",
        "./src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj",
    ],

    # Domain primitives (TenantId, ITenantContext): pure value-type code.
    # Routed only to its own dedicated unit-test project to keep Stryker's
    # baseline + coverage-capture phases small (a routing change reduced this
    # from ~97 unrelated tests → just the SharedKernel-targeting set).
    "src/Kartova.SharedKernel/stryker-config.json": [
        "./tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj",
    ],

    # JWT auth + claims transformation + tenant-scope endpoint filter.
    # Unit oracle in dedicated SharedKernel.AspNetCore.Tests (JwtAuthExt +
    # TenantClaimsTransformation tests).
    # End-to-end oracle in Api.IntegrationTests (real Keycloak realm).
    "src/Kartova.SharedKernel.AspNetCore/stryker-config.json": [
        "./tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj",
        "./tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj",
    ],

    # TenantScope, TenantScopeRequiredInterceptor, AddModuleDbContext —
    # require a real Postgres data source. Both module integration test
    # projects exercise these via their respective DbContext registrations.
    "src/Kartova.SharedKernel.Postgres/stryker-config.json": [
        "./src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj",
        "./src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj",
    ],

    # Catalog module: shared by all four Catalog source projects.
    "src/Modules/Catalog/stryker-config.json": [
        "./src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj",
        "./src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj",
    ],

    # Organization module: shared by all three Organization source projects.
    "src/Modules/Organization/stryker-config.json": [
        "./src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj",
        "./src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj",
    ],
}


def render(test_projects: list[str]) -> dict:
    cfg = copy.deepcopy(BASE)
    cfg["stryker-config"]["test-projects"] = list(test_projects)
    return cfg


def serialize(cfg: dict) -> str:
    # Two-space indent + trailing newline matches the existing root
    # `stryker-config.json` style and avoids spurious diffs.
    return json.dumps(cfg, indent=2) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--validate", action="store_true",
                    help="Exit non-zero if any on-disk config differs from the would-be-generated content.")
    args = ap.parse_args()

    drift: list[str] = []

    for cfg_rel, test_projects in ROUTES.items():
        target = REPO_ROOT / cfg_rel
        rendered = serialize(render(test_projects))

        if args.validate:
            if not target.exists():
                drift.append(f"missing: {cfg_rel}")
                continue
            actual = target.read_text(encoding="utf-8")
            if actual != rendered:
                drift.append(f"differs: {cfg_rel}")
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(rendered, encoding="utf-8")
            print(f"wrote: {cfg_rel}")

    if args.validate:
        if drift:
            print("Drift detected between routing manifest and on-disk stryker configs:", file=sys.stderr)
            for d in drift:
                print(f"  - {d}", file=sys.stderr)
            print("Run `python scripts/generate-stryker-configs.py` to regenerate.", file=sys.stderr)
            return 1
        print("OK: all on-disk stryker configs match the routing manifest.")
        return 0

    print(f"Done — wrote {len(ROUTES)} stryker configs.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
