#!/usr/bin/env bash
# project-stats.sh — deterministic codebase statistics from git-tracked files.
# Axes: language · role (production/test) · domain (business/infra/boilerplate/other)
# Deps: git + gawk (both ship with Git Bash). LOC = non-blank physical lines.
# Classification rules are documented in ../SKILL.md and live in classify() below.
set -euo pipefail

OUT=""
DEBUG=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out)   OUT="${2:?--out requires a path}"; shift 2 ;;
    --debug) DEBUG=1; shift ;;
    -h|--help) echo "usage: project-stats.sh [--out <file>] [--debug]"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

command -v gawk >/dev/null 2>&1 || { echo "project-stats: gawk required (ships with Git Bash)" >&2; exit 3; }
cd "$(git rev-parse --show-toplevel)"

# ── 1. classify every tracked path (pure string ops; no file reads) ──
#    TSV: path \t language \t kind(text|binary) \t role \t domain
classify() {
  git ls-files | gawk '
  {
    path=$0
    n=split(path, seg, "/"); base=seg[n]
    ext=""
    if (base ~ /\./) { m=split(base, p, "."); ext=tolower(p[m]) }

    # --- language ---
    lang="Config/Other"; kind="text"
    if      (ext=="cs")  lang="C#"
    else if (ext=="tsx") lang="TSX"
    else if (ext=="ts")  lang="TypeScript"
    else if (ext=="js"||ext=="mjs") lang="JavaScript"
    else if (ext=="md")  lang="Markdown"
    else if (ext=="yaml"||ext=="yml"||ext=="tpl"||base==".helmignore") lang="YAML/Helm"
    else if (ext=="json") lang="JSON"
    else if (ext=="sql")  lang="SQL"
    else if (ext=="html") lang="HTML"
    else if (ext=="css")  lang="CSS"
    else if (ext=="sh")   lang="Shell"
    else if (ext=="ps1")  lang="PowerShell"
    else if (ext=="py")   lang="Python"
    else if (ext=="csproj"||ext=="props"||ext=="slnx"||ext=="xml"||ext=="runsettings") lang="MSBuild/XML"
    else if (base=="Dockerfile"||base==".dockerignore") lang="Dockerfile"
    else if (base=="Makefile") lang="Makefile"
    else if (ext ~ /^(png|jpg|jpeg|gif|ico|svg|webp|woff|woff2|ttf|eot|pdf)$/) { lang="Asset"; kind="binary" }

    # --- role: production vs test (app-code languages only) ---
    code = (lang=="C#"||lang=="TSX"||lang=="TypeScript"||lang=="JavaScript"||lang=="SQL"||lang=="HTML"||lang=="CSS")
    if (path ~ /^docs\//) code=0      # docs/ (incl. mockup .html) are never production
    role="NonCode"
    if (code) {
      role="Production"
      if      (path ~ /(^|\/)tests\//)          role="Test"
      else if (path ~ /\.Tests\//)              role="Test"
      else if (path ~ /\.IntegrationTests\//)   role="Test"
      else if (path ~ /Kartova\.Testing\./)     role="Test"
      else if (path ~ /(^|\/)__tests__\//)      role="Test"
      else if (base ~ /\.(test|spec)\.[a-z]+$/) role="Test"
    }

    # --- domain (production only); FIRST MATCH WINS ---
    domain="-"
    if (role=="Production") {
      if      (path ~ /\/Migrations\// || base ~ /\.Designer\.cs$/ || base ~ /ModelSnapshot\.cs$/ || path ~ /(^|\/)web\/src\/generated\//) domain="Boilerplate"
      else if (path ~ /\.Domain\// || path ~ /\.Application\//) { if (base ~ /(Dto|Request|Response)\.cs$/) domain="Infrastructure"; else domain="Business" }
      else if (path ~ /(^|\/)web\/src\/(features|pages|hooks)\//) domain="Business"
      else if (path ~ /\.Infrastructure/ || path ~ /Kartova\.Api\// || path ~ /\.Contracts\// || base ~ /(Dto|Request|Response)\.cs$/ || path ~ /SharedKernel/ || path ~ /(^|\/)web\/src\//) domain="Infrastructure"
      else domain="Other"
    }
    printf "%s\t%s\t%s\t%s\t%s\n", path, lang, kind, role, domain
  }'
}

CLS="$(classify)"

if [[ "$DEBUG" == "1" ]]; then
  printf '%s\n' "$CLS"
  exit 0
fi

# ── 2. count non-blank + total physical lines for text files (batched) ──
#    TSV: path \t total \t nonblank
LOC="$(printf '%s\n' "$CLS" | gawk -F'\t' '$3=="text"{print $1}' \
  | tr '\n' '\0' | xargs -0 -r gawk '
      FNR==1 { if (f!="") print f"\t"tot"\t"nb; f=FILENAME; tot=0; nb=0 }
      { sub(/\r$/, ""); tot++ } NF { nb++ }
      END { if (f!="") print f"\t"tot"\t"nb }
  ')"

# ── 3. join + aggregate + render ──
render() {
  gawk -F'\t' '
    FNR==NR { tot[$1]=$2+0; nb[$1]=$3+0; next }   # LOC stream first
    {
      path=$1; lang=$2; kind=$3; role=$4; dom=$5
      files_total++; L=nb[path]; total_loc+=L
      lf[lang]++; ll[lang]+=L
      if (kind=="binary") asset_files++
      if (role=="Production"||role=="Test") { rf[role]++; rl[role]+=L; code_files++ }
      else { noncode_files++; noncode_loc+=L }
      if (role=="Production" && dom!="-") { df[dom]++; dl[dom]+=L; prod_loc+=L }
    }
    END {
      # ---- reconciliation (the in-script correctness assertion) ----
      s=0; for (k in lf) s+=lf[k]
      if (s!=files_total)                              { print "RECONCILE FAIL: language sum "s" != total "files_total > "/dev/stderr"; exit 1 }
      if ((rf["Production"]+rf["Test"])!=code_files)   { print "RECONCILE FAIL: role split"                      > "/dev/stderr"; exit 1 }
      ds=0; for (k in df) ds+=df[k]
      if (ds!=(rf["Production"]+0))                    { print "RECONCILE FAIL: domain sum "ds" != prod "(rf["Production"]+0) > "/dev/stderr"; exit 1 }

      # ---- headline ----
      bizpct=(prod_loc>0)?100*dl["Business"]/prod_loc:0
      printf "Headline:  %d files · %d LOC · %.0f%% business code (of production)\n\n", files_total, total_loc, bizpct

      # ---- language (sorted by LOC desc) ----
      PROCINFO["sorted_in"]="@val_num_desc"
      printf "Language          Files       LOC\n----------------------------------\n"
      for (k in ll) printf "%-16s %6d %9d\n", k, lf[k], ll[k]
      printf "%-16s %6d %9d\n\n", "TOTAL", files_total, total_loc

      # ---- role ----
      printf "Role              Files       LOC\n----------------------------------\n"
      printf "%-16s %6d %9d\n",   "Production", rf["Production"]+0, rl["Production"]+0
      printf "%-16s %6d %9d\n",   "Test",       rf["Test"]+0,       rl["Test"]+0
      printf "%-16s %6d %9d\n\n", "Non-code",   noncode_files+0,    noncode_loc+0

      # ---- domain (fixed order) ----
      printf "Domain (production)  Files       LOC   %% prod LOC\n------------------------------------------------\n"
      split("Business Infrastructure Boilerplate Other", ord, " ")
      for (i=1;i<=4;i++){ k=ord[i]; pc=(prod_loc>0)?100*dl[k]/prod_loc:0; printf "%-18s %6d %9d %8.0f%%\n", k, df[k]+0, dl[k]+0, pc }
      nb_files=df["Infrastructure"]+df["Boilerplate"]+df["Other"]+0
      nb_loc=dl["Infrastructure"]+dl["Boilerplate"]+dl["Other"]+0
      pc=(prod_loc>0)?100*nb_loc/prod_loc:0
      printf "%-18s %6d %9d %8.0f%%\n", "  non-business", nb_files, nb_loc, pc

      print "\nNotes: LOC = non-blank physical lines (comments included). The generated web"
      print "API client is gitignored and absent from this count. Classification is file-level."
    }
  ' <(printf '%s\n' "$LOC") <(printf '%s\n' "$CLS")
}

if [[ -n "$OUT" ]]; then render | tee "$OUT"; else render; fi
