#!/usr/bin/env node
// PreToolUse hook: enforces the Serena-first tool policy from CLAUDE.md.
//
//  - HARD BLOCK: built-in Edit / Write / MultiEdit / NotebookEdit on a code
//    file (.cs/.ts/.tsx) -> deny, redirect to the Serena symbolic editors.
//  - SOFT NUDGE: built-in Read / Glob / Grep touching code -> non-blocking
//    reminder to prefer get_symbols_overview / find_symbol / search_graph
//    (the built-ins stay available for the fallbacks CLAUDE.md allows).
//
// A hook only sees the path/pattern, not intent, so it cannot tell an allowed
// fallback from a lapse — hence reads/searches only nudge. Genuine edit
// fallbacks (Serena tried and failed, unparseable/generated file): set the
// env var SERENA_GUARD=0 to bypass for the session.

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
          permissionDecision: 'deny',
          permissionDecisionReason:
            `Serena-first policy (CLAUDE.md): '${tool}' is blocked on existing code file ${target}. ` +
            'Edit code through the Serena symbolic tools instead: replace_symbol_body, ' +
            'insert_before_symbol / insert_after_symbol, replace_content, or rename_symbol / ' +
            'move_symbol / safe_delete_symbol. (Creating a NEW .cs/.ts/.tsx file via Write is allowed — ' +
            'Serena cannot create files.) Built-in editors on existing code are allowed only when Serena ' +
            'was tried and failed, the file is unparseable, or it is generated. For a genuine fallback, ' +
            'set SERENA_GUARD=0 for this session and retry.',
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
