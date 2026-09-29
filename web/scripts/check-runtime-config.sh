#!/usr/bin/env bash
# Asserts a built SPA image renders /config.js from KARTOVA_* env at container start (TD-016).
# Usage: web/scripts/check-runtime-config.sh <image> <html-document>   e.g. kartova/web:ci index.html
set -euo pipefail
image="$1"; html="$2"
name="kartova-cfgcheck-$$"
body="$(mktemp)"; headers="$(mktemp)"

fail() { echo "check-runtime-config($image): $1" >&2; exit 1; }
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; rm -f "$body" "$headers"; }
trap cleanup EXIT

start() {  # start [docker-run env args...] — runs the image, waits for /config.js, sets $base
  docker rm -f "$name" >/dev/null 2>&1 || true
  docker run -d --name "$name" -p 127.0.0.1::8080 "$@" "$image" >/dev/null
  local portline; portline="$(docker port "$name" 8080/tcp | head -1)"
  [ -n "$portline" ] || fail "docker port returned nothing for $name (container may have exited immediately)"
  local port; port="$(printf '%s' "$portline" | sed 's/.*://')"
  [ -n "$port" ] || fail "could not parse a port from docker port output: $portline"
  base="http://127.0.0.1:$port"
  for _ in $(seq 1 30); do
    curl -fsS -o /dev/null "$base/config.js" 2>/dev/null && return 0
    if [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" != "true" ]; then
      docker logs "$name" >&2 || true
      fail "container exited before serving /config.js"
    fi
    sleep 1
  done
  docker logs "$name" >&2 || true
  fail "container never served /config.js"
}

# 1. Env set → values rendered, not cacheable, served as JS.
start -e KARTOVA_OIDC_AUTHORITY=https://kc.sentinel/realms/r \
      -e KARTOVA_OIDC_CLIENT_ID=sentinel-client \
      -e KARTOVA_API_BASE_URL=https://api.sentinel
curl -fsS -D "$headers" -o "$body" "$base/config.js"
grep -q '"oidcAuthority":"https://kc.sentinel/realms/r"' "$body" || fail "oidcAuthority not rendered: $(cat "$body")"
grep -q '"oidcClientId":"sentinel-client"' "$body"             || fail "oidcClientId not rendered: $(cat "$body")"
grep -q '"apiBaseUrl":"https://api.sentinel"' "$body"          || fail "apiBaseUrl not rendered: $(cat "$body")"
grep -qi '^cache-control: no-store' "$headers"                 || fail "config.js must be Cache-Control: no-store"
grep -qi '^content-type: application/javascript' "$headers"    || fail "config.js must be served as application/javascript"
curl -fsS -D "$headers" -o "$body" "$base/$html"
grep -q '<script src="/config.js"></script>' "$body"            || fail "$html does not load /config.js"
grep -qi "^content-security-policy:.*script-src 'self'" "$headers" || fail "$html response must carry a Content-Security-Policy with script-src 'self': $(cat "$headers")"

# 2. Env unset → Dockerfile "" defaults; never a literal ${KARTOVA_...} placeholder.
start
curl -fsS -o "$body" "$base/config.js"
grep -q '\${' "$body" && fail "unsubstituted placeholder in config.js: $(cat "$body")"
grep -q '"apiBaseUrl":""' "$body" || fail "unset env must render empty strings: $(cat "$body")"

echo "check-runtime-config($image): OK"
