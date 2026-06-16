#!/usr/bin/env node
// Stop hook: blocks the turn when the last assistant message contains a
// completion claim without evidence of verification. Enforces the
// Definition of Done from CLAUDE.md so "finished" means reviewed + tested + run.

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

const CLAIM_RE = /slice \d+ complete|implementation complete|all done|ready to merge|finished implementing|fully finished|implementation is (complete|finished|ready)|✅ done|\bdone\.$/im;
const EVIDENCE_RE = /docker compose|docker build|images (ci|job|build)|real[- ]seam|integration test|test suite|build green|suite green|treatwarningsaserrors|pending verification|staged|definition of done|verification pending|e2e smoke|end-to-end/i;

(async () => {
  const raw = await readAll(process.stdin);
  let input = {};
  try { input = JSON.parse(raw); } catch { process.exit(0); }

  const transcript = input.transcript_path;
  if (!transcript || !fs.existsSync(transcript)) process.exit(0);

  const text = lastAssistantText(transcript);
  if (!text) process.exit(0);

  if (!CLAIM_RE.test(text)) process.exit(0);
  if (EVIDENCE_RE.test(text)) process.exit(0);

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
    'Revise the claim to cite each bullet by command + output, or use "implementation staged, <step> pending verification" instead of "complete/done/ready to merge".',
  ].join('\n');

  process.stdout.write(JSON.stringify({ decision: 'block', reason }));
  process.exit(0);
})();
