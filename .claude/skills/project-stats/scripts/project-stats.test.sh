#!/usr/bin/env bash
# Regression test for project-stats.sh — asserts representative real files
# classify into the expected (language, role, domain) buckets.
set -uo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$DIR/project-stats.sh"

if [[ ! -x "$SCRIPT" ]]; then
  echo "engine not found / not executable: $SCRIPT" >&2
  exit 3
fi

# One scan, reused by every assertion.
ALL="$("$SCRIPT" --debug)" || { echo "engine --debug failed" >&2; exit 3; }

fail=0
assert() { # path  want_lang  want_role  want_domain
  local p="$1" el="$2" er="$3" ed="$4" row lang role dom
  row="$(printf '%s\n' "$ALL" | awk -F'\t' -v p="$p" '$1==p{print;f=1} END{exit f?0:9}')" \
    || { echo "MISSING from scan: $p"; fail=1; return; }
  IFS=$'\t' read -r _ lang _ role dom <<<"$row"
  if [[ "$lang" != "$el" || "$role" != "$er" || "$dom" != "$ed" ]]; then
    printf 'FAIL %s\n     got [%s/%s/%s] want [%s/%s/%s]\n' "$p" "$lang" "$role" "$dom" "$el" "$er" "$ed"
    fail=1
  else
    printf 'ok   %s -> %s/%s/%s\n' "$p" "$lang" "$role" "$dom"
  fi
}

assert "src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs"                            "C#"         "Production" "Business"
assert "src/Modules/Catalog/Kartova.Catalog.Application/AssignApplicationTeamCommand.cs"      "C#"         "Production" "Business"
assert "src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs"                "C#"         "Production" "Infrastructure"
assert "src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationCountByTeamReader.cs"  "C#"         "Production" "Infrastructure"
assert "src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs"              "C#"         "Test"       "-"
assert "web/src/features/auth/api/session.ts"                                                "TypeScript" "Production" "Business"
assert "web/src/features/auth/api/__tests__/session.test.tsx"                                "TSX"        "Test"       "-"

# A migration path is dynamic — resolve it, then assert it is generated boilerplate.
MIG="$(git ls-files 'src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/*.cs' | grep -v '\.Designer\.cs$' | head -1)"
if [[ -n "$MIG" ]]; then
  assert "$MIG" "C#" "Production" "Boilerplate"
else
  echo "WARN: no Catalog migration found to assert"; fail=1
fi

if [[ "$fail" -eq 0 ]]; then echo "ALL PASS"; else echo "TEST FAILURES"; exit 1; fi
