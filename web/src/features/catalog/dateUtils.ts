export const MS_PER_DAY = 24 * 60 * 60 * 1000;

/** YYYY-MM-DD string at UTC midnight `n` days from `now`. */
export function isoDateAtMidnight(now: number, daysFromNow: number): string {
  const d = new Date(now + daysFromNow * MS_PER_DAY);
  d.setUTCHours(0, 0, 0, 0);
  return d.toISOString();
}

/** Strip time portion: ISO string → "YYYY-MM-DD" for native `<input type="date">`. */
export function toDateInputValue(iso: string): string {
  if (!iso) return "";
  const idx = iso.indexOf("T");
  return idx >= 0 ? iso.slice(0, idx) : iso;
}

/** Inflate "YYYY-MM-DD" → UTC-midnight ISO string. */
export function fromDateInputValue(local: string): string {
  if (!local) return "";
  // Treat as UTC midnight so the wire value is unambiguous.
  return new Date(`${local}T00:00:00Z`).toISOString();
}
