#!/usr/bin/env bash
# Asserts the chart renders both SPA workloads and wires API CORS only when origins are set (S2, ADR-0118).
set -euo pipefail
chart="$(dirname "$0")/kartova"
cs="Host=x;Database=x;Username=x;Password=x"
out="$(mktemp)"

fail() { echo "render-check: $1" >&2; exit 1; }

helm template "$chart" --set database.connectionString="$cs" > "$out"
grep -q "app.kubernetes.io/component: web$" "$out"       || fail "web Deployment/Service missing by default"
grep -q "app.kubernetes.io/component: web-admin$" "$out" || fail "web-admin Deployment/Service missing by default"
grep -q "Cors__" "$out"                                  && fail "CORS env rendered without origins set"

helm template "$chart" --set database.connectionString="$cs" \
  --set web.origin=https://app.example --set webAdmin.origin=https://admin.example > "$out"
grep -A1 "name: Cors__AllowedOrigins__0" "$out"      | grep -q "https://app.example"   || fail "tenant CORS origin not wired"
grep -A1 "name: Cors__AdminAllowedOrigins__0" "$out" | grep -q "https://admin.example" || fail "admin CORS origin not wired"

helm template "$chart" --set database.connectionString="$cs" --set webAdmin.enabled=false > "$out"
grep -q "app.kubernetes.io/component: web-admin$" "$out" && fail "web-admin rendered while disabled"

echo "render-check: OK"
