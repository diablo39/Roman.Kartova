#!/usr/bin/env bash

set -euo pipefail

DETECT_ONLY=0
BASE_BRANCH="main"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--detect-only)
			DETECT_ONLY=1
			shift
			;;
		--base-branch)
			BASE_BRANCH="${2:-main}"
			shift 2
			;;
		*)
			echo "Unknown argument: $1" >&2
			exit 2
			;;
	esac
	done

print_kv() {
	printf '%s=%s\n' "$1" "$2"
}

PROJECT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$PROJECT_ROOT"

choose_dotnet_project() {
	local candidate
	candidate="$(find . \( -path '*/bin/*' -o -path '*/obj/*' -o -path '*/node_modules/*' \) -prune -o -type f -iname '*test*.csproj' -print | head -n 1)"
	if [[ -z "$candidate" ]]; then
		candidate="$(find . \( -path '*/bin/*' -o -path '*/obj/*' -o -path '*/node_modules/*' \) -prune -o -type f -iname '*.csproj' -print | head -n 1)"
	fi
	printf '%s' "$candidate"
}

choose_java_project() {
	find . \( -path '*/target/*' -o -path '*/node_modules/*' \) -prune -o -type f -name 'pom.xml' -print | head -n 1
}

choose_js_project() {
	local package_file
	while IFS= read -r package_file; do
		local package_dir
		package_dir="$(dirname "$package_file")"
		if find "$package_dir" -maxdepth 1 -type f \( -name 'jest.config.js' -o -name 'jest.config.cjs' -o -name 'jest.config.mjs' -o -name 'jest.config.ts' -o -name 'jest.config.json' \) | grep -q .; then
			printf '%s' "$package_file"
			return 0
		fi
		if grep -qi '"jest"' "$package_file"; then
			printf '%s' "$package_file"
			return 0
		fi
	done < <(find . \( -path '*/node_modules/*' -o -path '*/dist/*' -o -path '*/build/*' \) -prune -o -type f -name 'package.json' -print)
	return 1
}

choose_python_root() {
	local config_file
	config_file="$(find . \( -path '*/.venv/*' -o -path '*/venv/*' -o -path '*/node_modules/*' \) -prune -o -type f \( -name 'pyproject.toml' -o -name 'setup.cfg' -o -name 'pytest.ini' \) -print | head -n 1)"
	if [[ -z "$config_file" ]] && find . \( -path '*/.venv/*' -o -path '*/venv/*' -o -path '*/node_modules/*' \) -prune -o -type f -name '*.py' -print -quit | grep -q .; then
		config_file='.'
	fi
	printf '%s' "$config_file"
}

CHANGED_FILES="$(git diff --name-only "${BASE_BRANCH}...HEAD" 2>/dev/null || true)"
if [[ -z "$CHANGED_FILES" ]]; then
	print_kv status no-changes
	print_kv project_root "$PROJECT_ROOT"
	print_kv base_branch "$BASE_BRANCH"
	exit 0
fi

PROJECT_TYPE=""
COVERAGE_TOOL=""
RUN_DIR=""
COMMAND=""
REPORT_GLOB=""
MARKER=""

if MARKER="$(choose_dotnet_project)" && [[ -n "$MARKER" ]]; then
	PROJECT_TYPE=".NET"
	COVERAGE_TOOL="XPlat Code Coverage"
	RUN_DIR="$(cd "$(dirname "$MARKER")" && pwd)"
	COMMAND="dotnet test --collect:\"XPlat Code Coverage\" --results-directory ./coverage"
	REPORT_GLOB="coverage/*/coverage.cobertura.xml"
elif MARKER="$(choose_java_project)" && [[ -n "$MARKER" ]]; then
	PROJECT_TYPE="Java"
	COVERAGE_TOOL="JaCoCo"
	RUN_DIR="$(cd "$(dirname "$MARKER")" && pwd)"
	COMMAND="mvn test"
	REPORT_GLOB="target/site/jacoco/jacoco.xml"
elif MARKER="$(choose_js_project || true)" && [[ -n "$MARKER" ]]; then
	PROJECT_TYPE="JavaScript/TypeScript"
	COVERAGE_TOOL="Jest"
	RUN_DIR="$(cd "$(dirname "$MARKER")" && pwd)"
	COMMAND="npx jest --coverage --coverageReporters=json-summary"
	REPORT_GLOB="coverage/coverage-summary.json"
elif { command -v coverage >/dev/null 2>&1 || grep -Rqi '\[coverage:' pyproject.toml setup.cfg .coveragerc 2>/dev/null; } && MARKER="$(choose_python_root)" && [[ -n "$MARKER" ]]; then
	PROJECT_TYPE="Python"
	COVERAGE_TOOL="coverage.py"
	RUN_DIR="$(cd "$(dirname "$MARKER")" && pwd)"
	COMMAND="coverage run --branch -m pytest && coverage json"
	REPORT_GLOB="coverage.json"
else
	print_kv status unsupported
	print_kv project_root "$PROJECT_ROOT"
	print_kv base_branch "$BASE_BRANCH"
	print_kv supported ".NET:XPlat Code Coverage;Java:JaCoCo;JavaScript/TypeScript:Jest;Python:coverage.py"
	exit 1
fi

print_kv status ok
print_kv project_root "$PROJECT_ROOT"
print_kv base_branch "$BASE_BRANCH"
print_kv project_type "$PROJECT_TYPE"
print_kv coverage_tool "$COVERAGE_TOOL"
print_kv run_dir "$RUN_DIR"
print_kv marker "$MARKER"
print_kv command "$COMMAND"
print_kv report_glob "$REPORT_GLOB"

if [[ "$DETECT_ONLY" -eq 1 ]]; then
	exit 0
fi

set +e
(
	cd "$RUN_DIR"
	eval "$COMMAND"
)
EXIT_CODE=$?
set -e

print_kv exit_code "$EXIT_CODE"
exit "$EXIT_CODE"