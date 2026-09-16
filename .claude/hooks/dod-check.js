#!/usr/bin/env node
// Stop hook: blocks the turn when the last assistant message contains a
// completion claim without a DoD ledger citation (docs/superpowers/verification/<date>-<topic>/dod.md).
// Enforces the Definition of Done from CLAUDE.md so "finished" means reviewed + tested + run.

const fs = require('node:fs');
const path = require('node:path');

function readAll(stream) {
  return new Promise((resolve) => {
    const chunks = [];
    stream.on('data', (c) => chunks.push(c));
    stream.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    stream.on('error', () => resolve(''));
  });
}

function extractText(content) {
  if (typeof content === 'string') return content;
  if (Array.isArray(content)) {
    return content
      .filter((p) => p && p.type === 'text' && typeof p.text === 'string')
      .map((p) => p.text)
      .join('\n');
  }
  return '';
}

function lastAssistantText(transcriptPath) {
  let raw;
  try {
    raw = fs.readFileSync(transcriptPath, 'utf8');
  } catch {
    return '';
  }
  const lines = raw.split(/\r?\n/).filter(Boolean);
  for (let i = lines.length - 1; i >= 0; i--) {
    let obj;
    try { obj = JSON.parse(lines[i]); } catch { continue; }
    const msg = obj && obj.message;
    if (msg && msg.role === 'assistant') {
      const text = extractText(msg.content);
      if (text && text.trim()) return text;
    }
  }
  return '';
}

const CLAIM_RE = /slice( \d+)? complete|implementation complete|all done|ready to merge|finished implementing|fully finished|implementation is (complete|finished|ready)|✅ done|\bdone\.$/im;

// Quoted or code-span text is being *discussed*, not asserted — e.g. explaining what the
// hook matches, or citing a template. Blank it out before testing for a claim so those
// mentions don't self-trigger the gate.
function stripQuoted(text) {
  return text
    .replace(/```[\s\S]*?```/g, ' ')      // fenced code blocks
    .replace(/`[^`\n]*`/g, ' ')           // inline code spans
    .replace(/"[^"\n]*"/g, ' ')           // straight double quotes
    .replace(/[“][^”\n]*[”]/g, ' ');      // curly double quotes
}
// A completion claim must point at the slice's DoD ledger (the queryable record of gate status).
const LEDGER_RE = /superpowers[\/\\]verification[\/\\][^\s)"']+[\/\\]dod\.md/i;
// Capture the full cited dod.md path (including any leading docs/ prefix) so we can resolve
// its sibling gate-findings.yaml on disk.
const LEDGER_PATH_RE = /([^\s"'`()]*superpowers[\/\\]verification[\/\\][^\s"'`()]+[\/\\]dod\.md)/i;

// Extract the slice's terminal commit from the dod.md ledger. Matches the mandated
// "**Terminal commit:** <sha>" line (case-insensitive, tolerant of markdown/table syntax).
const DOD_TERMINAL_RE = /terminal[ _]commit[^0-9a-f]*([0-9a-f]{7,40})\b/i;
// The gate-findings.yaml top-level terminal_commit: field.
const GF_TERMINAL_RE = /^terminal_commit:\s*([0-9a-f]{7,40})\b/im;

// Resolve a cited (repo-relative or absolute) path against the hook's working directory,
// which Claude Code runs from the project root.
function resolveCited(citedPath) {
  const cleaned = citedPath.replace(/[\\]/g, '/');
  if (path.isAbsolute(cleaned) && fs.existsSync(cleaned)) return cleaned;
  const joined = path.resolve(process.cwd(), cleaned);
  return joined;
}

// Narrow completeness/consistency check on the slice's gate-findings.yaml, the machine-readable
// per-finding telemetry that sits beside dod.md (CLAUDE.md §Definition of Done — DoD ledger).
// Recurring meta-theme across slices: gate-findings.yaml left stale, truncated mid-way, or
// stamped with a terminal commit that no longer matches the ledger. This turns that
// LLM-re-caught drift into a deterministic gate. Returns null when fine, or a block reason.
function gateFindingsProblem(dodPath) {
  // Only enforce when the cited ledger actually resolves on disk — otherwise preserve the
  // prior lenient behavior (string-cite alone was accepted before this check existed).
  if (!fs.existsSync(dodPath)) return null;

  const gfPath = path.join(path.dirname(dodPath), 'gate-findings.yaml');
  if (!fs.existsSync(gfPath)) {
    return `The slice DoD ledger ${toRel(dodPath)} has no sibling gate-findings.yaml. ` +
      'Copy docs/superpowers/templates/gate-findings-template.yaml beside dod.md and record each gate before claiming completion.';
  }

  let gf, dod;
  try {
    gf = fs.readFileSync(gfPath, 'utf8');
    dod = fs.readFileSync(dodPath, 'utf8');
  } catch {
    return null; // fail-open on unexpected IO error; do not brick a legitimate claim
  }

  // (1) Not finalized: a completion-time gate-findings.yaml must carry a terminal_commit.
  const gfCommitMatch = gf.match(GF_TERMINAL_RE);
  if (!gfCommitMatch) {
    return `gate-findings.yaml (${toRel(gfPath)}) has no terminal_commit. ` +
      'A finalized ledger stamps the terminal commit; an unfilled one is not evidence of completion.';
  }

  // (2) Drift: gate-findings.yaml's terminal_commit must match dod.md's recorded terminal commit.
  const dodCommitMatch = dod.match(DOD_TERMINAL_RE);
  if (dodCommitMatch && dodCommitMatch[1].toLowerCase() !== gfCommitMatch[1].toLowerCase()) {
    return `Ledger drift: gate-findings.yaml terminal_commit=${gfCommitMatch[1]} but dod.md records ${dodCommitMatch[1]}. ` +
      'Re-run the terminal gates on the final commit and re-stamp both, or fix whichever is stale.';
  }

  // (3) Truncated mid-way: every recorded finding is a "- gate:" block that carries a "verdict:".
  // A file chopped mid-finding leaves a dangling gate with no verdict, so the counts diverge.
  const gateCount = (gf.match(/^\s*-\s*gate:/gim) || []).length;
  const verdictCount = (gf.match(/^\s*verdict:/gim) || []).length;
  if (gateCount !== verdictCount) {
    return `gate-findings.yaml (${toRel(gfPath)}) looks truncated: ${gateCount} finding(s) but ${verdictCount} verdict(s). ` +
      'Each finding needs gate + verdict; a mid-file cut drops the tail. Complete the file before claiming completion.';
  }

  return null;
}

function toRel(p) {
  try { return path.relative(process.cwd(), p).replace(/\\/g, '/') || p; } catch { return p; }
}

(async () => {
  const raw = await readAll(process.stdin);
  let input = {};
  try { input = JSON.parse(raw); } catch { process.exit(0); }

  const transcript = input.transcript_path;
  if (!transcript || !fs.existsSync(transcript)) process.exit(0);

  const text = lastAssistantText(transcript);
  if (!text) process.exit(0);

  if (!CLAIM_RE.test(stripQuoted(text))) process.exit(0);
  // A completion claim is only allowed when it cites the DoD ledger for the slice.
  // The ledger is the mandated record of per-gate status (CLAUDE.md §Definition of Done);
  // evidence keywords alone no longer suffice.
  if (LEDGER_RE.test(text)) {
    // The claim cites a ledger. When that ledger resolves on disk, additionally require its
    // sibling gate-findings.yaml to be present, finalized (terminal_commit), consistent with
    // dod.md, and not truncated mid-way. Block with a specific reason if not; otherwise allow.
    const pathMatch = text.match(LEDGER_PATH_RE);
    if (pathMatch) {
      const gfReason = gateFindingsProblem(resolveCited(pathMatch[1]));
      if (gfReason) {
        process.stdout.write(JSON.stringify({ decision: 'block', reason: gfReason }));
        process.exit(0);
      }
    }
    process.exit(0);
  }

  const reason = [
    'Completion claim detected without verification evidence. Definition of Done (CLAUDE.md) — the ten always-blocking gates:',
    '  1. Full solution build green with TreatWarningsAsErrors=true.',
    '  2. Per-task subagent reviews (spec-compliance + code-quality) executed — no skipping on grounds of "trivial".',
    '  3. Full test suite green: unit + architecture + integration; wiring slices must include real-seam coverage (real JwtBearer/KeyCloak + real Postgres/RLS, never mocked).',
    '  4. Container build green: the images CI job (docker compose build); manual docker compose up is smoke, not evidence.',
    '  5. /simplify applied; should-fix items addressed or skipped with reason.',
    '  6-8. requesting-code-review, review-pr, deep-review on final code.',
    '  Then re-run build + full suite and confirm still green.',
    '  9. Visual / API verification against the running system (ADR-0084); N/A only when the diff has no runtime surface.',
    '  10. CI green on the PR (terminal); scripts/ci-local.sh is the required pre-push mirror.',
    '',
    'Record each gate in the slice DoD ledger and CITE it in the claim:',
    '  docs/superpowers/verification/<date>-<topic>/dod.md',
    '  (copy docs/superpowers/templates/dod-ledger-template.md if it does not exist yet).',
    'Or, if not actually done, say "implementation staged, <step> pending verification" instead of "complete/done/ready to merge".',
  ].join('\n');

  process.stdout.write(JSON.stringify({ decision: 'block', reason }));
  process.exit(0);
})();
