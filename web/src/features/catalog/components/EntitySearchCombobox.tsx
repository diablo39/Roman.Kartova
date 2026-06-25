import { useEffect, useId, useRef, useState } from "react";
import { useEntitySearch, type EntityOption } from "@/features/catalog/api/relationships";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const MIN_QUERY_LENGTH = 2;
const DEBOUNCE_MS = 250;

interface Props {
  kind: RelationshipKind;
  excludeId?: string;
  onSelect: (entity: EntityOption) => void;
  placeholder?: string;
}

const optionId = (prefix: string, i: number) => `${prefix}-option-${i}`;

export function EntitySearchCombobox({ kind, excludeId, onSelect, placeholder }: Props) {
  const listboxId = useId();
  const containerRef = useRef<HTMLDivElement>(null);
  const [q, setQ] = useState("");
  const [debouncedQ, setDebouncedQ] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);

  useEffect(() => {
    const id = window.setTimeout(() => setDebouncedQ(q), DEBOUNCE_MS);
    return () => window.clearTimeout(id);
  }, [q]);

  const enabled = debouncedQ.length >= MIN_QUERY_LENGTH;
  const search = useEntitySearch(kind, debouncedQ, { enabled });
  const results = (search.data ?? []).filter((e) => e.id !== excludeId);
  const showDropdown = open && enabled;

  useEffect(() => {
    if (!open) return;
    const onDocMouseDown = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
  }, [open]);

  // Reset active highlight when the query or visibility changes (render-time guard).
  const [prevQ, setPrevQ] = useState(debouncedQ);
  const [prevShow, setPrevShow] = useState(showDropdown);
  if (debouncedQ !== prevQ || showDropdown !== prevShow) {
    setPrevQ(debouncedQ);
    setPrevShow(showDropdown);
    setActiveIndex(null);
  }

  const select = (e: EntityOption) => {
    onSelect(e);
    setQ("");
    setDebouncedQ("");
    setOpen(false);
    setActiveIndex(null);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Escape") { e.preventDefault(); setQ(""); setDebouncedQ(""); setOpen(false); setActiveIndex(null); return; }
    if (!showDropdown || results.length === 0) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActiveIndex((p) => (p === null ? 0 : Math.min(p + 1, results.length - 1))); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setActiveIndex((p) => (p === null ? 0 : Math.max(p - 1, 0))); }
    else if (e.key === "Enter" && activeIndex !== null && results[activeIndex]) { e.preventDefault(); select(results[activeIndex]); }
  };

  const activeDescendant = showDropdown && activeIndex !== null && results[activeIndex] ? optionId(listboxId, activeIndex) : undefined;

  return (
    <div ref={containerRef} className="relative w-full">
      <input
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-controls={listboxId}
        aria-expanded={showDropdown}
        aria-activedescendant={activeDescendant}
        value={q}
        placeholder={placeholder ?? `Search ${kind.toLowerCase()}s…`}
        onChange={(e) => { setQ(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
        className="w-full rounded-lg border border-secondary bg-primary px-3 py-2 text-sm text-primary shadow-xs outline-none placeholder:text-tertiary focus:border-brand-500 focus:ring-1 focus:ring-brand-500"
      />
      {showDropdown && (
        <div id={listboxId} role="listbox" className="absolute z-10 mt-1 w-full overflow-hidden rounded-lg border border-secondary bg-primary shadow-lg">
          {search.isLoading && <div className="px-3 py-2 text-sm text-tertiary">Searching…</div>}
          {!search.isLoading && search.isError && <div className="px-3 py-2 text-sm text-error-primary">Search failed. Try again.</div>}
          {!search.isLoading && !search.isError && results.length === 0 && <div className="px-3 py-2 text-sm text-tertiary">No matches.</div>}
          {!search.isLoading && !search.isError && results.map((e, i) => (
            <button
              key={e.id}
              id={optionId(listboxId, i)}
              type="button"
              role="option"
              aria-selected={i === activeIndex}
              onClick={() => select(e)}
              onMouseEnter={() => setActiveIndex(i)}
              className={`block w-full px-3 py-2 text-left text-sm ${i === activeIndex ? "bg-secondary" : ""}`}
            >
              {e.displayName}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
