export type SpecKind = "rendered" | "other";

/**
 * Classify a stored spec document as Scalar-renderable (OpenAPI/Swagger or AsyncAPI)
 * or not. Deliberately cheap and dependency-free (no YAML parser): it only chooses the
 * DEFAULT view. Rendering correctness is guaranteed by SpecRender's error boundary, not here.
 */
export function detectSpecKind(content: string | null | undefined, _mediaType?: string): SpecKind {
  if (!content || content.trim() === "") return "other";

  // Primary: structured JSON with a top-level openapi/swagger/asyncapi string key.
  try {
    const doc = JSON.parse(content) as unknown;
    if (doc !== null && typeof doc === "object") {
      const rec = doc as Record<string, unknown>;
      if (typeof rec.openapi === "string" || typeof rec.swagger === "string" || typeof rec.asyncapi === "string")
        return "rendered";
    }
    return "other";
  } catch {
    // Not JSON (likely YAML) — cheap head scan of the first ~4 KB.
    const head = content.slice(0, 4096);
    if (/^\s*(openapi|swagger|asyncapi)\s*:/m.test(head)) return "rendered";
    return "other";
  }
}
