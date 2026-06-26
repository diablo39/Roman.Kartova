#!/usr/bin/env node
// Stop hook: blocks the turn when the last assistant message contains a
// completion claim without a DoD ledger citation (docs/superpowers/verification/<date>-<topic>/dod.md).
// Enforces the Definition of Done from CLAUDE.md so "finished" means reviewed + tested + run.

const fs = require('node:fs');

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
// A completion claim must point at the slice's DoD ledger (the queryable record of gate status).
const LEDGER_RE = /superpowers[\/\\]verification[\/\\][^\s)"']+[\/\\]dod\.md/i;

(async () => {
  const raw = await readAll(process.stdin);
  let input = {};
  try { input = JSON.parse(raw); } catch { process.exit(0); }

  const transcript = input.transcript_path;
  if (!transcript || !fs.existsSync(transcript)) process.exit(0);

  const text = lastAssistantText(transcript);
  if (!text) process.exit(0);

  if (!CLAIM_RE.test(text)) process.exit(0);
  // A completion claim is only allowed when it cites the DoD ledger for the slice.
  // The ledger is the mandated record of per-gate status (CLAUDE.md §Definition of Done);
  // evidence keywords alone no longer suffice.
  if (LEDGER_RE.test(text)) process.exit(0);

  const reason = [
    'Completion claim detected without verification evidence. Definition of Done (CLAUDE.md) — the eight always-blocking gates (gate 6 is conditional):',
    '  1. Full solution build green with TreatWarningsAsErrors=true.',
    '  2. Per-task subagent reviews (spec-compliance + code-quality) executed — no skipping on grounds of "trivial".',
    '  3. Full test suite green: unit + architecture + integration; wiring slices must include real-seam coverage (real JwtBearer/KeyCloak + real Postgres/RLS, never mocked).',
    '  4. Container build green: the images CI job (docker compose build); manual docker compose up is smoke, not evidence.',
    '  5. /simplify applied; should-fix items addressed or skipped with reason.',
    '  6. Mutation loop (mutation-sentinel -> test-generator) — conditional: blocking only for Domain/Application logic changes, else should-do.',
    '  7-9. requesting-code-review, review-pr, deep-review on final code.',
    '  Then re-run build + full suite and confirm still green.',
    '',
    'Record each gate in the slice DoD ledger and CITE it in the claim:',
    '  docs/superpowers/verification/<date>-<topic>/dod.md',
    '  (copy docs/superpowers/templates/dod-ledger-template.md if it does not exist yet).',
    'Or, if not actually done, say "implementation staged, <step> pending verification" instead of "complete/done/ready to merge".',
  ].join('\n');

  process.stdout.write(JSON.stringify({ decision: 'block', reason }));
  process.exit(0);
})();
