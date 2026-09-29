import { describe, it, expect } from "vitest";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

// ADR-0118 (amended 2026-09-28): the admin console shares UI-kit source with the tenant SPA, but tenant
// code must never reach the admin bundle, and tenant code must never import the admin app.
const SRC = path.resolve(import.meta.dirname, "..", "src");
const ADMIN = path.join(SRC, "admin") + path.sep;
const ENTRY = path.join(SRC, "admin", "main.tsx");
const FORBIDDEN = [
  path.join(SRC, "features") + path.sep,
  path.join(SRC, "app") + path.sep,
  path.join(SRC, "shared", "auth") + path.sep,
];
const SPECIFIER =
  /(?:import|export)\s[^'"]*?from\s*["']([^"']+)["']|import\s*\(\s*["']([^"']+)["']\s*\)|import\s*["']([^"']+)["']/g;
const CANDIDATE_SUFFIXES = ["", ".ts", ".tsx", "/index.ts", "/index.tsx"];
const isSource = (f: string) => /\.(ts|tsx)$/.test(f) && !/\.test\.(ts|tsx)$/.test(f);
const rel = (f: string) => path.relative(SRC, f).split(path.sep).join("/");

function resolveSpecifier(fromFile: string, spec: string): string | null {
  let base: string;
  if (spec.startsWith("@/")) base = path.join(SRC, spec.slice(2));
  else if (spec.startsWith(".")) base = path.resolve(path.dirname(fromFile), spec);
  else return null; // bare package import
  for (const suffix of CANDIDATE_SUFFIXES) {
    const candidate = base + suffix;
    if (existsSync(candidate) && statSync(candidate).isFile()) return candidate;
  }
  return null;
}

function specifiersOf(file: string): string[] {
  const text = readFileSync(file, "utf8");
  return [...text.matchAll(SPECIFIER)].map((m) => m[1] ?? m[2] ?? m[3]).filter((s): s is string => !!s);
}

function importClosure(entry: string): Set<string> {
  const seen = new Set<string>();
  const stack = [entry];
  while (stack.length > 0) {
    const file = stack.pop()!;
    if (seen.has(file)) continue;
    seen.add(file);
    if (!isSource(file)) continue; // e.g. .css — part of the closure, but not scanned
    for (const spec of specifiersOf(file)) {
      const resolved = resolveSpecifier(file, spec);
      if (resolved) stack.push(resolved);
    }
  }
  return seen;
}

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = path.join(dir, name);
    return statSync(full).isDirectory() ? sourceFiles(full) : isSource(full) ? [full] : [];
  });
}

describe("admin import boundary (ADR-0118)", () => {
  it("the admin entry's transitive import closure contains no tenant code", () => {
    const closure = [...importClosure(ENTRY)];

    // Guards against a resolver that silently resolves nothing (the test would then pass vacuously).
    expect(closure.map(rel)).toContain("shared/oidc/authConfig.ts");
    expect(closure.map(rel)).toContain("shared/api/createAuthedApiClient.ts");

    const offenders = closure.filter((f) => FORBIDDEN.some((dir) => f.startsWith(dir))).map(rel);
    expect(offenders).toEqual([]);
  });

  it("no file outside src/admin imports the admin app", () => {
    const offenders = sourceFiles(SRC)
      .filter((f) => !f.startsWith(ADMIN))
      .flatMap((f) =>
        specifiersOf(f)
          .filter((s) => resolveSpecifier(f, s)?.startsWith(ADMIN))
          .map((s) => `${rel(f)} → ${s}`),
      );
    expect(offenders).toEqual([]);
  });

  // Positive controls: the two guards above are only meaningful if the machinery behind them
  // actually works — an over-eager resolver (or a regex that misses a form) would let both
  // pass vacuously with zero offenders. Prove each piece directly.
  it("the specifier regex extracts every import/export/dynamic-import form", () => {
    const sample = [
      'import x from "a";',
      'export { y } from "b";',
      'import("c");',
      'import "d";',
    ].join("\n");

    const specifiers = [...sample.matchAll(SPECIFIER)].map((m) => m[1] ?? m[2] ?? m[3]);

    expect(specifiers).toEqual(["a", "b", "c", "d"]);
  });

  it("resolveSpecifier resolves an @/ alias to a real file under the forbidden features dir (path-separator handling on this OS)", () => {
    const resolved = resolveSpecifier(ENTRY, "@/features/catalog/api/client");

    expect(resolved).not.toBeNull();
    expect(resolved!.startsWith(FORBIDDEN[0]!)).toBe(true);
  });
});
