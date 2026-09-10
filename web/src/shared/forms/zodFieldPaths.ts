import type { ZodType } from "zod";

/**
 * Enumerates the dotted field paths a Zod object schema declares — e.g.
 * `registerVmSchema` → `{ "displayName", "description", "teamId", "provider",
 * "attributes.powerState", "attributes.os", … }`.
 *
 * Used to tell {@link applyProblemDetailsToForm} which server error keys map to a real,
 * registered form field. A key that is NOT in this set must never be treated as "handled":
 * `setError` on an unregistered field is a silent no-op, so a 400 whose key matched nothing
 * would otherwise vanish with no toast and no field highlight (TD-003).
 *
 * Nesting mirrors react-hook-form field paths: a nested object contributes both its own path
 * (`attributes`) and each descendant (`attributes.os`), because the server may report an error
 * at either level. Optional / nullable / default wrappers are unwrapped to reach the inner
 * shape. Non-object leaves (strings, numbers, enums, arrays) contribute only their own path.
 */
// Schemas are module-level constants whose field set never changes, but the callers invoke this
// on the (cold) 400-error path per submit. Memoize the top-level result per schema so the
// recursive walk runs once per schema, not once per error. Keyed weakly so a schema that is GC'd
// (e.g. a locally-derived .omit()/.pick() schema) does not leak.
const cache = new WeakMap<ZodType, Set<string>>();

export function zodFieldPaths(schema: ZodType, prefix = ""): Set<string> {
  // Only the un-prefixed (top-level) call is cacheable; recursive calls carry a prefix and are
  // cheap tail work under the cached root.
  if (prefix === "") {
    const cached = cache.get(schema);
    if (cached) return cached;
  }
  const out = new Set<string>();
  collect(schema, prefix, out);
  if (prefix === "") cache.set(schema, out);
  return out;
}

function collect(schema: unknown, prefix: string, out: Set<string>): void {
  // Unwrap optional / nullable / default / readonly wrappers, which carry the real schema
  // under `def.innerType` (Zod v4). Guard against a cycle with a bounded loop.
  let node: { def?: { innerType?: unknown; shape?: unknown }; shape?: unknown } | undefined =
    schema as never;
  for (let depth = 0; node?.def?.innerType && depth < 16; depth++) {
    node = node.def.innerType as never;
  }
  if (node === undefined || node === null) return;

  // ZodObject exposes its members via the `shape` getter (Zod v4); fall back to `def.shape`.
  const rawShape = node.shape ?? node.def?.shape;
  const shape = typeof rawShape === "function" ? (rawShape as () => unknown)() : rawShape;
  if (!shape || typeof shape !== "object") return;

  for (const [key, child] of Object.entries(shape as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key;
    out.add(path);
    collect(child, path, out);
  }
}
