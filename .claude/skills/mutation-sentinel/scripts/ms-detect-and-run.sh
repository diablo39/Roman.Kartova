#!/usr/bin/env bash

set -euo pipefail

DETECT_ONLY=0
ANALYZE_ONLY=0
BASE_BRANCH=""
HELPER_STRATEGY="single-report"
EXPECTED_REPORT_COUNT=1
MANIFEST_PATH=""

while [[ $# -gt 0 ]]; do
	case "$1" in
		--detect-only)
			DETECT_ONLY=1
			shift
			;;
		--analyze-only)
			ANALYZE_ONLY=1
			shift
			;;
		--base-branch)
			BASE_BRANCH="${2}"
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

write_manifest() {
	local manifest_file="$1"
	local report_count="$2"
	local exit_code="$3"
	local started_at="$4"
	local report_paths="$5"

	mkdir -p "$(dirname "$manifest_file")"
	{
		printf 'run_started_at_utc=%s\n' "$started_at"
		printf 'project_type=%s\n' "$PROJECT_TYPE"
		printf 'tool_name=%s\n' "$TOOL_NAME"
		printf 'run_dir=%s\n' "$RUN_DIR"
		printf 'helper_strategy=%s\n' "$HELPER_STRATEGY"
		printf 'expected_report_count=%s\n' "$EXPECTED_REPORT_COUNT"
		printf 'report_count=%s\n' "$report_count"
		printf 'exit_code=%s\n' "$exit_code"
		while IFS= read -r report_path; do
			[[ -z "$report_path" ]] && continue
			printf 'report_path=%s\n' "$report_path"
		done <<< "$report_paths"
	} > "$manifest_file"
}

to_windows_native_path() {
	local input_path="$1"

	if [[ -z "$input_path" ]]; then
		return 0
	fi

	if [[ "$input_path" =~ ^/mnt/([a-zA-Z])/(.*)$ ]]; then
		local drive_letter="${BASH_REMATCH[1]}"
		local relative_path="${BASH_REMATCH[2]//\//\\}"
		printf '%s:\\%s' "$(printf '%s' "$drive_letter" | tr '[:lower:]' '[:upper:]')" "$relative_path"
		return 0
	fi

	if [[ "$input_path" =~ ^/([a-zA-Z])/(.*)$ ]]; then
		local drive_letter="${BASH_REMATCH[1]}"
		local relative_path="${BASH_REMATCH[2]//\//\\}"
		printf '%s:\\%s' "$(printf '%s' "$drive_letter" | tr '[:lower:]' '[:upper:]')" "$relative_path"
		return 0
	fi

	printf '%s' "$input_path"
}

resolve_dotnet_stryker_runner() {
	local candidate=""

	for candidate in \
		"$(command -v dotnet-stryker.exe 2>/dev/null || true)" \
		"$(command -v dotnet-stryker 2>/dev/null || true)" \
		"${HOME:-}/.dotnet/tools/dotnet-stryker.exe" \
		"${HOME:-}/.dotnet/tools/dotnet-stryker"
	do
		if [[ -n "$candidate" && -f "$candidate" ]]; then
			printf '"%s"' "$candidate"
			return 0
		fi
	done

	if command -v dotnet >/dev/null 2>&1; then
		printf 'dotnet stryker'
		return 0
	fi

	return 1
}

# ── Config parsing ──────────────────────────────────────────────────

CONFIG_FILE_NAME="mutation-targets.json"

detect_json_parser() {
	if command -v jq >/dev/null 2>&1; then
		printf 'jq'
	elif command -v python3 >/dev/null 2>&1; then
		printf 'python3'
	elif command -v python >/dev/null 2>&1 \
		&& python -c "import sys; sys.exit(0 if sys.version_info[0]>=3 else 1)" 2>/dev/null; then
		printf 'python'
	elif command -v powershell.exe >/dev/null 2>&1; then
		printf 'powershell'
	else
		return 1
	fi
}

validate_json() {
	local config_path="$1"
	local parser="$2"
	case "$parser" in
		jq)
			jq empty "$config_path" 2>&1
			;;
		python3|python)
			"$parser" -c "import json,sys; json.load(open(sys.argv[1]))" "$config_path" 2>&1
			;;
		powershell)
			powershell.exe -NoProfile -Command \
				"Get-Content '${config_path}' -Raw | ConvertFrom-Json | Out-Null" 2>&1
			;;
		*)
			echo "Internal error: unknown parser '${parser}'" >&2
			return 1
			;;
	esac
}

validate_config_structure() {
	local config_path="$1"
	local parser="$2"

	case "$parser" in
		jq)
			jq -e '
				if (.groups | type) != "array" then error("groups must be an array")
				elif (.groups | length) == 0 then error("groups must not be empty")
				else .groups | to_entries[] | .key as $gi |
					if (.value.stack | type) != "string" then
						error("groups[\($gi)].stack is required")
					elif (.value.projects | type) != "array" then
						error("groups[\($gi)].projects must be an array")
					elif (.value.projects | length) == 0 then
						error("groups[\($gi)].projects must not be empty")
					else .value.projects | to_entries[] |
						if (.value.path | type) != "string" or (.value.path | length) == 0 then
							error("groups[\($gi)].projects[\(.key)].path is required")
						else empty end
					end
				end
			' "$config_path" 2>&1 >/dev/null
			;;
		python3|python)
			"$parser" -c "
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
if not isinstance(cfg.get('groups'), list):
    print('groups must be an array'); sys.exit(1)
if len(cfg['groups']) == 0:
    print('groups must not be empty'); sys.exit(1)
for gi, g in enumerate(cfg['groups']):
    if not isinstance(g.get('stack'), str):
        print(f'groups[{gi}].stack is required'); sys.exit(1)
    if not isinstance(g.get('projects'), list):
        print(f'groups[{gi}].projects must be an array'); sys.exit(1)
    if len(g['projects']) == 0:
        print(f'groups[{gi}].projects must not be empty'); sys.exit(1)
    for pi, p in enumerate(g['projects']):
        if not isinstance(p.get('path'), str) or len(p['path']) == 0:
            print(f'groups[{gi}].projects[{pi}].path is required'); sys.exit(1)
" "$config_path" 2>&1
			;;
		powershell)
			powershell.exe -NoProfile -Command "
\$cfg = Get-Content '${config_path}' -Raw | ConvertFrom-Json
if (-not \$cfg.groups -or \$cfg.groups.Count -eq 0) {
    Write-Error 'groups must be a non-empty array'; exit 1
}
for (\$gi = 0; \$gi -lt \$cfg.groups.Count; \$gi++) {
    \$g = \$cfg.groups[\$gi]
    if (-not \$g.stack) { Write-Error \"groups[\$gi].stack is required\"; exit 1 }
    if (-not \$g.projects -or \$g.projects.Count -eq 0) {
        Write-Error \"groups[\$gi].projects must be a non-empty array\"; exit 1
    }
    for (\$pi = 0; \$pi -lt \$g.projects.Count; \$pi++) {
        \$p = \$g.projects[\$pi]
        if (-not \$p.path) {
            Write-Error \"groups[\$gi].projects[\$pi].path is required\"; exit 1
        }
    }
}
" 2>&1
			;;
		*)
			echo "Internal error: unknown parser '${parser}'" >&2
			return 1
			;;
	esac
}

flatten_config() {
	local config_path="$1"
	local parser="$2"
	case "$parser" in
		jq)
			jq -r '
				.groups[] |
				.stack as $stack |
				(.solution // "") as $sol |
				(.configFile // "") as $gcfg |
				.projects[] |
				[$stack, $sol, $gcfg, .path, (.name // ""), (.configFile // "")] | @tsv
			' "$config_path"
			;;
		python3|python)
			"$parser" -c "
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
for g in cfg['groups']:
    stack = g['stack']
    sol = g.get('solution', '')
    gcfg = g.get('configFile', '')
    for p in g['projects']:
        print('\t'.join([stack, sol, gcfg, p['path'], p.get('name', ''), p.get('configFile', '')]))
" "$config_path"
			;;
		powershell)
			powershell.exe -NoProfile -Command "
\$cfg = Get-Content '${config_path}' -Raw | ConvertFrom-Json
foreach (\$g in \$cfg.groups) {
    \$stack = \$g.stack
    \$sol = if (\$g.solution) { \$g.solution } else { '' }
    \$gcfg = if (\$g.configFile) { \$g.configFile } else { '' }
    foreach (\$p in \$g.projects) {
        \$name = if (\$p.name) { \$p.name } else { '' }
        \$pcfg = if (\$p.configFile) { \$p.configFile } else { '' }
        Write-Output \"\$stack\`t\$sol\`t\$gcfg\`t\$(\$p.path)\`t\$name\`t\$pcfg\"
    }
}
"
			;;
		*)
			echo "Internal error: unknown parser '${parser}'" >&2
			return 1
			;;
	esac
}

count_groups() {
	local config_path="$1"
	local parser="$2"
	case "$parser" in
		jq)
			jq '.groups | length' "$config_path"
			;;
		python3|python)
			"$parser" -c "import json,sys; print(len(json.load(open(sys.argv[1]))['groups']))" "$config_path"
			;;
		powershell)
			powershell.exe -NoProfile -Command \
				"(Get-Content '${config_path}' -Raw | ConvertFrom-Json).groups.Count"
			;;
		*)
			echo "Internal error: unknown parser '${parser}'" >&2
			return 1
			;;
	esac
}

resolve_project_name() {
	local path="$1"
	local explicit_name="${2:-}"

	if [[ -n "$explicit_name" ]]; then
		printf '%s' "$explicit_name"
		return 0
	fi

	local base
	base="$(basename "$path")"
	# Strip extension if present; for directories use the directory name as-is
	if [[ "$base" == *.* ]]; then
		printf '%s' "${base%.*}"
	else
		printf '%s' "$base"
	fi
}

# ── Main flow ───────────────────────────────────────────────────────

PROJECT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$PROJECT_ROOT"

# Resolve the default branch when the caller did not supply one explicitly.
if [[ -z "$BASE_BRANCH" ]]; then
	BASE_BRANCH="$(git symbolic-ref refs/remotes/origin/HEAD 2>/dev/null \
		| sed 's|refs/remotes/origin/||')" || true
fi
if [[ -z "$BASE_BRANCH" ]]; then
	BASE_BRANCH="$(git remote show origin 2>/dev/null \
		| awk '/HEAD branch/{print $NF}')" || true
fi
if [[ -z "$BASE_BRANCH" ]]; then
	BASE_BRANCH="main"
fi

# Determine mutation mode: full when on the default branch, incremental otherwise.
CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")"
if [[ "$CURRENT_BRANCH" == "$BASE_BRANCH" ]]; then
	MODE="full"
else
	MODE="incremental"
	if [[ "$ANALYZE_ONLY" -eq 0 ]]; then
		CHANGED_FILES="$(git diff --name-only "${BASE_BRANCH}...HEAD" 2>/dev/null || true)"
		if [[ -z "$CHANGED_FILES" ]]; then
			print_kv status no-changes
			print_kv project_root "$PROJECT_ROOT"
			print_kv base_branch "$BASE_BRANCH"
			print_kv mode "$MODE"
			exit 0
		fi
	fi
fi

# ── Read and validate config ────────────────────────────────────────

CONFIG_JSON="${PROJECT_ROOT}/${CONFIG_FILE_NAME}"
if [[ ! -f "$CONFIG_JSON" ]]; then
	print_kv status error
	print_kv error config-not-found
	print_kv error_message "mutation-targets.json not found at ${PROJECT_ROOT}. Create one — see schema: { \"groups\": [{ \"stack\": \".NET\", \"projects\": [{ \"path\": \"src/Foo/Foo.csproj\" }] }] }"
	exit 1
fi

JSON_PARSER="$(detect_json_parser)" || {
	print_kv status error
	print_kv error tool-not-found
	print_kv error_message "No JSON parser found. Install jq, python3, or ensure powershell.exe is available."
	exit 1
}

PARSE_ERR=""
PARSE_ERR="$(validate_json "$CONFIG_JSON" "$JSON_PARSER")" || {
	print_kv status error
	print_kv error config-parse-error
	print_kv error_message "Invalid JSON in ${CONFIG_FILE_NAME}: ${PARSE_ERR}"
	exit 1
}

STRUCT_ERR=""
STRUCT_ERR="$(validate_config_structure "$CONFIG_JSON" "$JSON_PARSER")" || {
	print_kv status error
	print_kv error config-invalid
	print_kv error_message "${STRUCT_ERR}"
	exit 1
}

GROUP_COUNT="$(count_groups "$CONFIG_JSON" "$JSON_PARSER" | tr -d '\r')"
# tr -d '\r' strips Windows CRLF from Python's print() output. Without this
# the last TSV field on each row carries a trailing CR, breaking file-existence
# checks for paths read from configFile / project_cfg.
CONFIG_LINES="$(flatten_config "$CONFIG_JSON" "$JSON_PARSER" | tr -d '\r')"

# ── Build commands from config ──────────────────────────────────────

PROJECT_TYPE=""
TOOL_NAME=""
RUN_DIR="$PROJECT_ROOT"
COMMAND=""
REPORT_GLOB=""
PROJECT_NAMES=""
PROJECT_COUNT=0
SEEN_NAMES=""
SOURCE_PROJECTS=""
SOLUTION_FILE=""
STRYKER_RUNNER=""

SUPPORTED_STACKS=".NET, Java, JavaScript/TypeScript, Python"
RUN_TIMESTAMP="$(date -u +%Y-%m-%d.%H-%M-%S 2>/dev/null || date +%Y-%m-%d.%H-%M-%S)"

while IFS=$'\t' read -r stack solution group_cfg path name project_cfg; do
	[[ -z "$path" ]] && continue

	# Resolve project name: explicit > filename stem > last path component
	RESOLVED_NAME="$(resolve_project_name "$path" "$name")"

	# Check for duplicate resolved names (they share StrykerOutput/)
	if [[ -n "$SEEN_NAMES" ]] && printf '%s\n' "$SEEN_NAMES" | grep -qxF "$RESOLVED_NAME"; then
		print_kv status error
		print_kv error config-invalid
		print_kv error_message "Duplicate project name '${RESOLVED_NAME}'. Add explicit 'name' field to disambiguate."
		exit 1
	fi
	SEEN_NAMES="${SEEN_NAMES}${SEEN_NAMES:+$'\n'}${RESOLVED_NAME}"

	# Validate project path exists
	if [[ ! -e "${PROJECT_ROOT}/${path}" ]]; then
		print_kv status error
		print_kv error config-path-not-found
		print_kv error_message "Project path not found: ${path}"
		exit 1
	fi

	# Resolve effective config file: project-level > group-level > none
	EFFECTIVE_CFG=""
	EFFECTIVE_CFG_REL=""
	if [[ -n "$project_cfg" ]]; then
		EFFECTIVE_CFG="${PROJECT_ROOT}/${project_cfg}"
		EFFECTIVE_CFG_REL="$project_cfg"
	elif [[ -n "$group_cfg" ]]; then
		EFFECTIVE_CFG="${PROJECT_ROOT}/${group_cfg}"
		EFFECTIVE_CFG_REL="$group_cfg"
	fi
	if [[ -n "$EFFECTIVE_CFG" && ! -f "$EFFECTIVE_CFG" ]]; then
		print_kv status error
		print_kv error config-path-not-found
		print_kv error_message "Tool config file not found: ${EFFECTIVE_CFG_REL}"
		exit 1
	fi

	# Build stack-specific command
	case "$stack" in
		".NET")
			PROJECT_TYPE=".NET"
			TOOL_NAME="Stryker.NET"

			# Resolve Stryker runner once per run
			if [[ -z "$STRYKER_RUNNER" ]]; then
				if ! STRYKER_RUNNER="$(resolve_dotnet_stryker_runner)"; then
					print_kv status error
					print_kv error tool-not-found
					print_kv error_message "Stryker.NET not found. Install: dotnet tool install -g dotnet-stryker"
					exit 1
				fi
			fi

			# Solution arg
			SOL_ARG=""
			CMD_RUN_DIR="$PROJECT_ROOT"
			if [[ -n "$solution" ]]; then
				SOL_ABS="${PROJECT_ROOT}/${solution}"
				if [[ ! -f "$SOL_ABS" ]]; then
					print_kv status error
					print_kv error config-path-not-found
					print_kv error_message "Solution file not found: ${solution}"
					exit 1
				fi
				SOLUTION_FILE="$solution"
				CMD_RUN_DIR="$(cd "$(dirname "$SOL_ABS")" && pwd)"
				if [[ "$STRYKER_RUNNER" == *".exe"* ]]; then
					SOL_ABS="$(to_windows_native_path "$SOL_ABS")"
				fi
				SOL_ARG="--solution \"${SOL_ABS}\""
			fi

			# Config arg
			CFG_ARG=""
			if [[ -n "$EFFECTIVE_CFG" ]]; then
				CFG_ABS="$(cd "$(dirname "$EFFECTIVE_CFG")" && pwd)/$(basename "$EFFECTIVE_CFG")"
				if [[ "$STRYKER_RUNNER" == *".exe"* ]]; then
					CFG_ABS="$(to_windows_native_path "$CFG_ABS")"
				fi
				CFG_ARG="--config-file \"${CFG_ABS}\""
			fi

			# Output directory: StrykerOutput/{project_name}/{timestamp}
			OUTPUT_DIR="${PROJECT_ROOT}/StrykerOutput/${RESOLVED_NAME}/${RUN_TIMESTAMP}"
			if [[ "$STRYKER_RUNNER" == *".exe"* ]]; then
				OUTPUT_DIR="$(to_windows_native_path "$OUTPUT_DIR")"
			fi
			OUTPUT_ARG="--output \"${OUTPUT_DIR}\""

			# Project file for --project flag
			PROJ_FILE="$(basename "$path")"

			# Build command wrapped in subshell with cd to solution/project dir
			if [[ "$MODE" == "full" ]]; then
				CMD_PART="(cd \"${CMD_RUN_DIR}\" && ${STRYKER_RUNNER} ${CFG_ARG} ${SOL_ARG} --project \"${PROJ_FILE}\" ${OUTPUT_ARG} --reporter json --reporter html)"
			else
				CMD_PART="(cd \"${CMD_RUN_DIR}\" && ${STRYKER_RUNNER} ${CFG_ARG} --since:${BASE_BRANCH} ${SOL_ARG} --project \"${PROJ_FILE}\" ${OUTPUT_ARG} --reporter json --reporter html)"
			fi

			REPORT_GLOB="StrykerOutput/*/*/reports/mutation-report.json"
			;;

		"Java")
			PROJECT_TYPE="Java"
			TOOL_NAME="PITest"
			PROJ_DIR="$(cd "${PROJECT_ROOT}/$(dirname "$path")" && pwd)"
			OUTPUT_DIR="${PROJECT_ROOT}/StrykerOutput/${RESOLVED_NAME}/${RUN_TIMESTAMP}"

			if [[ "$MODE" == "full" ]]; then
				CMD_PART="(cd \"${PROJ_DIR}\" && mvn test-compile org.pitest:pitest-maven:mutationCoverage -DreportsDirectory=\"${OUTPUT_DIR}\")"
			else
				CMD_PART="(cd \"${PROJ_DIR}\" && mvn test-compile org.pitest:pitest-maven:scmMutationCoverage -DreportsDirectory=\"${OUTPUT_DIR}\")"
			fi

			REPORT_GLOB="StrykerOutput/*/*/mutations.xml"
			;;

		"JavaScript/TypeScript")
			PROJECT_TYPE="JavaScript/TypeScript"
			TOOL_NAME="StrykerJS"
			PROJ_DIR="$(cd "${PROJECT_ROOT}/$(dirname "$path")" && pwd)"
			OUTPUT_DIR="${PROJECT_ROOT}/StrykerOutput/${RESOLVED_NAME}/${RUN_TIMESTAMP}"

			if [[ "$MODE" == "full" ]]; then
				CMD_PART="(cd \"${PROJ_DIR}\" && npx stryker run --htmlReporter.baseDir \"${OUTPUT_DIR}\" --jsonReporter.fileName \"${OUTPUT_DIR}/mutation.json\")"
			else
				CMD_PART="(cd \"${PROJ_DIR}\" && npx stryker run --incremental --htmlReporter.baseDir \"${OUTPUT_DIR}\" --jsonReporter.fileName \"${OUTPUT_DIR}/mutation.json\")"
			fi

			REPORT_GLOB="StrykerOutput/*/*/mutation.json"
			;;

		"Python")
			PROJECT_TYPE="Python"
			TOOL_NAME="mutmut"
			PROJ_DIR="$(cd "${PROJECT_ROOT}/$(dirname "$path")" && pwd)"

			CMD_PART="(cd \"${PROJ_DIR}\" && mutmut run --paths-to-mutate=src/)"

			REPORT_GLOB="mutmut-cli"
			;;

		*)
			print_kv status error
			print_kv error config-invalid
			print_kv error_message "Unsupported stack '${stack}'. Supported: ${SUPPORTED_STACKS}"
			exit 1
			;;
	esac

	# Chain commands with &&
	if [[ -n "$COMMAND" ]]; then
		COMMAND="${COMMAND} && ${CMD_PART}"
	else
		COMMAND="$CMD_PART"
	fi

	SOURCE_PROJECTS="${SOURCE_PROJECTS}${SOURCE_PROJECTS:+$'\n'}$(basename "$path")"
	PROJECT_NAMES="${PROJECT_NAMES}${PROJECT_NAMES:+,}${RESOLVED_NAME}"
	PROJECT_COUNT=$((PROJECT_COUNT + 1))
done <<< "$CONFIG_LINES"

# Set strategy based on project count
if [[ "$PROJECT_COUNT" -le 1 ]]; then
	HELPER_STRATEGY="single-report"
else
	HELPER_STRATEGY="per-project-reports"
fi
EXPECTED_REPORT_COUNT="$PROJECT_COUNT"

RUN_DIR="$PROJECT_ROOT"
MANIFEST_PATH="${PROJECT_ROOT}/StrykerOutput/mutation-sentinel-gh-last-run.manifest"

# ── Output key-value pairs ──────────────────────────────────────────

print_kv status ok
print_kv project_root "$PROJECT_ROOT"
print_kv base_branch "$BASE_BRANCH"
print_kv mode "$MODE"
print_kv project_type "$PROJECT_TYPE"
print_kv tool_name "$TOOL_NAME"
print_kv run_dir "$RUN_DIR"
print_kv solution_file "${SOLUTION_FILE:-}"
print_kv config_file "$CONFIG_JSON"
print_kv source_projects "${SOURCE_PROJECTS:-}"
print_kv source_project_count "$PROJECT_COUNT"
print_kv helper_strategy "$HELPER_STRATEGY"
print_kv expected_report_count "$EXPECTED_REPORT_COUNT"
print_kv manifest_path "$MANIFEST_PATH"
print_kv command "$COMMAND"
print_kv report_glob "$REPORT_GLOB"
print_kv group_count "$GROUP_COUNT"
print_kv project_names "$PROJECT_NAMES"

if [[ "$DETECT_ONLY" -eq 1 ]]; then
	exit 0
fi

# ── Analyze-only mode ───────────────────────────────────────────────

if [[ "$ANALYZE_ONLY" -eq 1 ]]; then
	REPORT_PATH=""
	REPORT_COUNT=0
	if [[ -f "$MANIFEST_PATH" ]]; then
		MANIFEST_REPORT_PATHS="$(grep '^report_path=' "$MANIFEST_PATH" 2>/dev/null | sed 's/^report_path=//')"
		MANIFEST_REPORT_COUNT="$(grep '^report_count=' "$MANIFEST_PATH" 2>/dev/null | tail -n1 | sed 's/^report_count=//')"
		if [[ -n "$MANIFEST_REPORT_PATHS" ]]; then
			while IFS= read -r candidate_path; do
				[[ -z "$candidate_path" ]] && continue
				if [[ -f "$candidate_path" ]]; then
					REPORT_PATH="$candidate_path"
					REPORT_COUNT=$((REPORT_COUNT + 1))
				fi
			done <<< "$MANIFEST_REPORT_PATHS"
			if [[ "$REPORT_COUNT" -gt 0 ]]; then
				print_kv analyze_status ok
				print_kv report_path "$REPORT_PATH"
				print_kv report_count "${MANIFEST_REPORT_COUNT:-$REPORT_COUNT}"
				print_kv manifest_path "$MANIFEST_PATH"
				exit 0
			fi
		fi
	fi

	if [[ "$REPORT_GLOB" != "mutmut-cli" ]]; then
		REPORT_PATH="$(find "$PROJECT_ROOT" -path "*/${REPORT_GLOB}" -type f 2>/dev/null \
			| xargs ls -t 2>/dev/null | head -n1)"
		REPORT_COUNT="$(find "$PROJECT_ROOT" -path "*/${REPORT_GLOB}" -type f 2>/dev/null \
			| wc -l | tr -d ' ')"
	else
		if [[ -f "${PROJECT_ROOT}/.mutmut-cache" ]]; then
			REPORT_PATH="${PROJECT_ROOT}/.mutmut-cache"
			REPORT_COUNT=1
		fi
	fi

	if [[ -z "$REPORT_PATH" ]]; then
		print_kv analyze_status no-report
		print_kv analyze_hint "No mutation report found matching '${REPORT_GLOB}' under '${PROJECT_ROOT}'. Run the mutation tool first, then re-invoke with --analyze-only."
		exit 1
	fi

	print_kv analyze_status ok
	print_kv report_path "$REPORT_PATH"
	print_kv report_count "$REPORT_COUNT"
	print_kv manifest_path "$MANIFEST_PATH"
	exit 0
fi

# ── Execute ─────────────────────────────────────────────────────────

RUN_STARTED_AT_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || true)"
RUN_ANCHOR_FILE="$(mktemp 2>/dev/null || printf '%s/.mutation-sentinel-gh-anchor-%s' "$PROJECT_ROOT" "$$")"
touch "$RUN_ANCHOR_FILE"

print_kv run_started_at_utc "$RUN_STARTED_AT_UTC"

set +e
(
	cd "$PROJECT_ROOT"
	eval "$COMMAND"
)
EXIT_CODE=$?
set -e

REPORT_PATHS=""
REPORT_COUNT=0
if [[ "$REPORT_GLOB" != "mutmut-cli" ]]; then
	while IFS= read -r report_path; do
		[[ -z "$report_path" ]] && continue
		REPORT_PATHS="${REPORT_PATHS}${REPORT_PATHS:+$'\n'}${report_path}"
		REPORT_COUNT=$((REPORT_COUNT + 1))
	done < <(find "$PROJECT_ROOT" -path "*/${REPORT_GLOB}" -type f -newer "$RUN_ANCHOR_FILE" 2>/dev/null | sort)
else
	if [[ -f "${PROJECT_ROOT}/.mutmut-cache" ]]; then
		REPORT_PATHS="${PROJECT_ROOT}/.mutmut-cache"
		REPORT_COUNT=1
	fi
fi

write_manifest "$MANIFEST_PATH" "$REPORT_COUNT" "$EXIT_CODE" "$RUN_STARTED_AT_UTC" "$REPORT_PATHS"
rm -f "$RUN_ANCHOR_FILE"

print_kv exit_code "$EXIT_CODE"
print_kv report_count "$REPORT_COUNT"
print_kv manifest_path "$MANIFEST_PATH"
exit "$EXIT_CODE"
