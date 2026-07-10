export type SpecKind = "openapi" | "other";

/**
 * Classify a stored spec document as OpenAPI-shaped or not. Deliberately cheap
 * and dependency-free (no YAML parser): it only chooses the DEFAULT view.
 * Rendering correctness is guaranteed by OpenApiRender's error boundary, not here.
 */
export function detectSpecKind(content: string | null | undefined, _mediaType?: string): SpecKind {
  if (!content || content.trim() === "") return "other";

  // Primary: structured JSON with a top-level openapi/swagger string key.
  try {
    const doc = JSON.parse(content) as Record<string, unknown>;
    if (typeof doc.openapi === "string" || typeof doc.swagger === "string") return "openapi";
    return "other";
  } catch {
    // Not JSON (likely YAML) — cheap head scan of the first ~4 KB.
    const head = content.slice(0, 4096);
    if (/^\s*(openapi|swagger)\s*:/m.test(head)) return "openapi";
    return "other";
  }
}
