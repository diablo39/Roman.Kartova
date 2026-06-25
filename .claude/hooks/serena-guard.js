#!/usr/bin/env node
// PreToolUse hook: enforces the Serena-first tool policy from CLAUDE.md.
//
//  - SOFT NUDGE (all): built-in Edit / Write / Read / Glob / Grep on a code
//    file (.cs/.ts/.tsx) -> non-blocking reminder to prefer Serena's symbolic
//    tools for navigation / impact / multi-symbol edits. Built-ins stay fully
//    available for small localized edits and quick reads (per the CLAUDE.md
//    tool-selection policy — preference, not mandate).
//
// A hook only sees the path/pattern, not intent, so it nudges rather than
// blocks — the agent decides whether Serena or a built-in fits the task.
// Set the env var SERENA_GUARD=0 to silence the nudges for a session.

const path = require('node:path');
const fs = require('node:fs');

function readAll(stream) {
  return new Promise((resolve) => {
    const chunks = [];
    stream.on('data', (c) => chunks.push(c));
    stream.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    stream.on('error', () => resolve(''));
  });
}

const CODE_EXT = new Set(['.cs', '.ts', '.tsx']);

// Paths the policy exempts: generated code, build output, deps, EF migrations,
// type declarations, and *.config.* — Serena's symbolic tools add no value here.
const EXEMPT_RE =
  /[\\/](obj|bin|node_modules|Migrations|dist|build)[\\/]|\.g\.cs$|\.Designer\.cs$|\.d\.ts$|\.config\.(ts|js|mjs|cjs)$|GlobalUsings/i;

function isCodePath(p) {
  if (!p || typeof p !== 'string') return false;
  if (EXEMPT_RE.test(p)) return false;
  return CODE_EXT.has(path.extname(p).toLowerCase());
}

function patternMentionsCode(s) {
  return typeof s === 'string' && /\.(cs|ts|tsx)\b/i.test(s);
}

function emit(payload) {
  process.stdout.write(
    JSON.stringify({ hookSpecificOutput: { hookEventName: 'PreToolUse', ...payload } }),
  );
  process.exit(0);
}

const EDIT_TOOLS = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit']);
const SEARCH_TOOLS = new Set(['Glob', 'Grep']);

(async () => {
  if (process.env.SERENA_GUARD === '0') process.exit(0);

  const raw = await readAll(process.stdin);
  let input = {};
  try { input = JSON.parse(raw); } catch { process.exit(0); }

  const tool = input.tool_name;
  const ti = input.tool_input || {};

  if (EDIT_TOOLS.has(tool)) {
    const target = ti.file_path || ti.notebook_path;
    if (isCodePath(target)) {
      // Serena has no create-file primitive, so creating a brand-new code file via Write is
      // legitimate and outside the symbolic-edit mapping. Only block edits to files that ALREADY
      // exist (where replace_symbol_body / insert_* / replace_content apply).
      const creatingNewFile = tool === 'Write' && !fs.existsSync(target);
      if (!creatingNewFile) {
        emit({
          additionalContext:
            `Serena-first (CLAUDE.md): for a structural or multi-symbol change to ${target}, prefer the ` +
            'Serena editors (replace_symbol_body / insert_before_symbol / insert_after_symbol / ' +
            'replace_content / rename_symbol / move_symbol). For a small, localized edit the built-in ' +
            `${tool} is fine. (New .cs/.ts/.tsx files via Write are always fine — Serena cannot create files.)`,
        });
      }
    }
    process.exit(0);
  }

  if (tool === 'Read') {
    if (isCodePath(ti.file_path)) {
      emit({
        additionalContext:
          `Serena-first policy (CLAUDE.md): ${ti.file_path} is a code file — prefer ` +
          'get_symbols_overview (structure) or find_symbol with include_body=true (a specific ' +
          'symbol) over a full Read. A plain Read is fine for a few lines or when a symbolic ' +
          'read cannot express what you need.',
      });
    }
    process.exit(0);
  }

  if (SEARCH_TOOLS.has(tool)) {
    const sig = [ti.pattern, ti.glob, ti.path, ti.file_pattern, ti.type].join(' ');
    if (patternMentionsCode(sig) || isCodePath(ti.path)) {
      emit({
        additionalContext:
          `Serena-first policy (CLAUDE.md): for symbols, callers, or implementations in code, ` +
          'prefer search_graph / find_symbol / find_referencing_symbols (or the roslyn-codelens ' +
          `tools for C#). ${tool} is acceptable as a discovery step for cross-file regex that ` +
          'the symbolic tools cannot express — then follow up matched code files with Serena.',
      });
    }
    process.exit(0);
  }

  process.exit(0);
})();
