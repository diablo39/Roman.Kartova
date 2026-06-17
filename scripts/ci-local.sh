#!/usr/bin/env bash
# Local mirror of .github/workflows/ci.yml — runs the SAME commands each CI job
# runs, in the SAME `--configuration Release`, so a push is unlikely to surface
# anything the local run didn't.
#
# What this CANNOT catch:
#   - the exact ubuntu-latest runner environment (this runs on your host)
#   - nondeterministic / flaky tests (e.g. concurrency races that pass locally
#     but fail under different timing) — rerun the suite or fix determinism
#
# Usage:
#   scripts/ci-local.sh                 # run all jobs
#   scripts/ci-local.sh backend images  # run only the named jobs
#   Jobs: backend images stryker frontend helm
#
# Run from the repo root (Git Bash on Windows is fine).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

JOBS=("$@"); [ ${#JOBS[@]} -eq 0 ] && JOBS=(backend images stryker frontend helm)
declare -A RESULT
FAILED=0

want() { for j in "${JOBS[@]}"; do [ "$j" = "$1" ] && return 0; done; return 1; }

run_job() {  # run_job <name> <function>
  local name="$1" fn="$2"
  printf '\n========== JOB: %s ==========\n' "$name"
  if "$fn"; then RESULT[$name]="PASS"; else RESULT[$name]="FAIL"; FAILED=1; fi
}

job_backend() {  # ci.yml: restore -> build Release -> test Release --no-build
  cmd //c "dotnet restore Kartova.slnx" \
  && cmd //c "dotnet build Kartova.slnx --configuration Release --no-restore" \
  && cmd //c "dotnet test Kartova.slnx --configuration Release --no-build --verbosity normal"
}

job_images() {  # ci.yml: compose build migrator+api, then web image
  docker compose build migrator api \
  && docker build -f web/Dockerfile -t kartova/web:ci web
}

job_stryker() {  # ci.yml: validate per-module + root Stryker configs
  python3 scripts/generate-stryker-configs.py --validate
}

job_frontend() {  # ci.yml: npm ci -> codegen -> typecheck -> build (in web/)
  ( cd web \
    && npm ci \
    && npm run codegen \
    && npm run typecheck \
    && npm run build )
}

job_helm() {  # ci.yml: lint + template with a dummy connection string
  local cs="Host=x;Database=x;Username=x;Password=x"
  helm lint deploy/helm/kartova/ --set database.connectionString="$cs" \
  && helm template deploy/helm/kartova/ --set database.connectionString="$cs" > /tmp/kartova-rendered.yaml
}

want backend  && run_job backend  job_backend
want images   && run_job images   job_images
want stryker  && run_job stryker  job_stryker
want frontend && run_job frontend job_frontend
want helm     && run_job helm     job_helm

printf '\n========== SUMMARY ==========\n'
for j in "${JOBS[@]}"; do printf '  %-10s %s\n' "$j" "${RESULT[$j]:-skipped}"; done
[ "$FAILED" -eq 0 ] && echo "All selected CI jobs passed." || echo "One or more CI jobs FAILED."
exit "$FAILED"
