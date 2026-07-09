#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root

echo "==> Bringing up the stack (pg, keycloak, migrator, api, web)"
docker compose up -d --build postgres keycloak-db keycloak migrator api web

# Poll a URL until it responds or the attempt budget is exhausted.
wait_for() {  # wait_for <url> <tries> <label>
  local url=$1 tries=$2 label=$3
  for ((i = 1; i <= tries; i++)); do
    curl -sf "$url" >/dev/null && return 0
    sleep 2
  done
  echo "$label not ready after $((tries * 2))s"
  return 1
}

echo "==> Waiting for API readiness"
wait_for http://localhost:8080/health/ready 60 "API" || { docker compose logs api; exit 1; }

echo "==> Waiting for web"
wait_for http://localhost:4173/ 30 "web" || { docker compose logs web; exit 1; }

echo "==> Running Playwright"
cd e2e
npm ci

if [ "$(uname)" = "Linux" ]; then
  npx playwright install --with-deps chromium
else
  npx playwright install chromium
fi

npx playwright test "$@"
