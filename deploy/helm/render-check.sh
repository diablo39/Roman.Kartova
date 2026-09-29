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

helm template "$chart" --set database.connectionString="$cs" --set web.enabled=false > "$out"
grep -q "app.kubernetes.io/component: web$" "$out" && fail "web rendered while disabled"
grep -q "app.kubernetes.io/component: web-admin$" "$out" || fail "web-admin missing while only web is disabled"

helm template "$chart" --set database.connectionString="$cs" \
  --set web.config.apiBaseUrl=https://api.example --set webAdmin.config.oidcClientId=kartova-admin-web > "$out"
grep -A1 "name: KARTOVA_API_BASE_URL" "$out"   | grep -q "https://api.example"  || fail "web runtime apiBaseUrl not wired"
grep -A1 "name: KARTOVA_OIDC_CLIENT_ID" "$out" | grep -q "kartova-admin-web"    || fail "web-admin runtime oidcClientId not wired"
[ "$(grep -c "name: KARTOVA_OIDC_AUTHORITY" "$out")" -eq 2 ] || fail "KARTOVA_OIDC_AUTHORITY must render on both SPA Deployments"

echo "render-check: OK"
