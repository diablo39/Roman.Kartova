#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root

echo "==> Bringing up the stack (pg, keycloak, migrator, api, web)"
docker compose up -d --build postgres keycloak-db keycloak migrator api web

echo "==> Waiting for API readiness"
for i in $(seq 1 60); do
  curl -sf http://localhost:8080/health/ready >/dev/null && break
  sleep 2
  [ "$i" = 60 ] && { echo "API not ready"; docker compose logs api; exit 1; }
done

echo "==> Waiting for web"
for i in $(seq 1 30); do
  curl -sf http://localhost:4173/ >/dev/null && break
  sleep 2
  [ "$i" = 30 ] && { echo "web not ready"; exit 1; }
done

echo "==> Running Playwright"
cd e2e
npm ci

if [ "$(uname)" = "Linux" ]; then
  npx playwright install --with-deps chromium
else
  npx playwright install chromium
fi

npx playwright test "$@"
