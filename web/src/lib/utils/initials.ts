export function initialsOf(displayName: string | undefined | null): string {
  if (!displayName) return "?";
  const parts = displayName.split(/\s+/).filter(Boolean).slice(0, 2);
  if (parts.length === 0) return "?";
  return parts.map((p) => p[0]?.toUpperCase()).join("");
}
