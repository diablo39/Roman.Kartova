import { useEffect, useId, useRef, useState } from "react";

import { useUserSearch, type UserSummaryResponse } from "@/features/users/api/users";
import { cx } from "@/lib/utils/cx";

/**
 * Stable prefix for option DOM ids. Combined with `useId()` so multiple
 * combobox instances on the same page don't collide when wiring
 * `aria-activedescendant`. We keep the index in the suffix because the
 * listbox is rendered in result order.
 */
function buildOptionId(prefix: string, index: number) {
  return `${prefix}-option-${index}`;
}

interface Props {
  /** Fires once the user picks a result from the dropdown. */
  onSelect: (user: UserSummaryResponse) => void;
  /** Placeholder for the visible input. */
  placeholder?: string;
}

/**
 * Min query length before we hit the server. Keeps single-char queries from
 * triggering a 422-prone search and matches the spec §6.8 guidance.
 */
const MIN_QUERY_LENGTH = 2;

/** Debounce window (ms) before the server-side search fires. */
const DEBOUNCE_MS = 250;

/**
 * Typeahead combobox that resolves a typed query against
 * `useUserSearch`. Hand-rolled (rather than `react-aria-components`'s
 * `<ComboBox>`) because our wire is a *search* against the server, not a
 * static list with client filtering — Aria's combobox assumes the latter.
 *
 * Behavior:
 *   - Local `q` state, debounced via `useEffect(setTimeout)` cleanup (250 ms).
 *   - `useUserSearch` is gated by `debouncedQ.length >= 2`, so single-char
 *     queries never reach the server.
 *   - Dropdown opens when the input is focused AND there's something to show
 *     (results, loading, or "no matches"). Selecting a row fires `onSelect`
 *     and clears the input — the caller decides what to do next (add to
 *     team, navigate, etc.).
 *   - ARIA combobox pattern: input has `role=combobox` + `aria-controls` →
 *     a `role=listbox` populated with `role=option` rows.
 *   - Keyboard navigation per WAI-ARIA APG 1.2: ArrowDown/Up move the active
 *     descendant (input keeps focus; `aria-activedescendant` points to the
 *     active option's id), Enter selects the active option, Escape clears
 *     and closes. Hover also promotes the active descendant so mouse and
 *     keyboard agree on which row Enter would select.
 *   - Click-outside closes the dropdown (mousedown listener on document so
 *     the click that *opens* a different focus target wins).
 */
export function UserSearchCombobox({ onSelect, placeholder = "Search users…" }: Props) {
  const listboxId = useId();
  const containerRef = useRef<HTMLDivElement>(null);

  const [q, setQ] = useState("");
  const [debouncedQ, setDebouncedQ] = useState("");
  const [open, setOpen] = useState(false);
  // Index of the keyboard-active option (null = no active highlight).
  // Reset whenever the result set or open state changes so a stale index
  // from a prior search can't point past the end of the new results.
  const [activeIndex, setActiveIndex] = useState<number | null>(null);

  // Debounce: clear the timer on each keystroke so only the *trailing* edge
  // fires the search. The cleanup runs on q-change AND on unmount, which is
  // the same React pattern used by the InvitationsPage live-now interval.
  useEffect(() => {
    const id = window.setTimeout(() => setDebouncedQ(q), DEBOUNCE_MS);
    return () => window.clearTimeout(id);
  }, [q]);

  const searchEnabled = debouncedQ.length >= MIN_QUERY_LENGTH;
  const search = useUserSearch(debouncedQ, { enabled: searchEnabled });

  // Close on outside click. Using mousedown (not click) so the dropdown closes
  // before any focus-stealing target inside the click region steals focus.
  useEffect(() => {
    if (!open) return;
    const onDocMouseDown = (e: MouseEvent) => {
      if (!containerRef.current) return;
      if (!containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
  }, [open]);

  const handleSelect = (user: UserSummaryResponse) => {
    onSelect(user);
    setQ("");
    setDebouncedQ("");
    setOpen(false);
    setActiveIndex(null);
  };

  const results = search.data ?? [];
  // Dropdown is "active" when the user is interacting with the box AND has
  // typed enough to meaningfully render something — keeps the focus->no-query
  // gap from flashing a noisy empty dropdown.
  const showDropdown = open && searchEnabled;

  // Reset the active highlight whenever the result set identity changes (new
  // search, results cleared) or the dropdown closes. Without this, an index
  // captured against the previous query could point past the end of the new
  // result array. We key on `debouncedQ` rather than `results` to avoid
  // resetting when React Query refetches with the same query string.
  useEffect(() => {
    setActiveIndex(null);
  }, [debouncedQ, showDropdown]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Escape") {
      e.preventDefault();
      setQ("");
      setDebouncedQ("");
      setOpen(false);
      setActiveIndex(null);
      return;
    }
    // The remaining keys only make sense when the listbox is rendered with
    // selectable options. Bail early so typing into a closed/empty dropdown
    // doesn't swallow native input behavior (caret moves, form submit, etc.).
    if (!showDropdown || results.length === 0) return;

    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActiveIndex((prev) =>
        prev === null ? 0 : Math.min(prev + 1, results.length - 1),
      );
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      // Clamp at 0 — APG allows wrapping but we prefer the more predictable
      // "stay at the top" behavior for a search-driven list.
      setActiveIndex((prev) => (prev === null || prev <= 0 ? 0 : prev - 1));
    } else if (e.key === "Enter") {
      if (activeIndex !== null && results[activeIndex]) {
        e.preventDefault();
        handleSelect(results[activeIndex]);
      }
      // If no option is active, fall through so Enter can submit a parent form.
    }
  };

  const activeDescendantId =
    showDropdown && activeIndex !== null && results[activeIndex]
      ? buildOptionId(listboxId, activeIndex)
      : undefined;

  return (
    <div ref={containerRef} className="relative w-full">
      <input
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-controls={listboxId}
        aria-expanded={showDropdown}
        aria-activedescendant={activeDescendantId}
        value={q}
        placeholder={placeholder}
        onChange={(e) => {
          setQ(e.target.value);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={handleKeyDown}
        className="w-full rounded-lg border border-secondary bg-primary px-3 py-2 text-sm text-primary shadow-xs outline-none placeholder:text-tertiary focus:border-brand-500 focus:ring-1 focus:ring-brand-500"
      />

      {showDropdown && (
        <div
          id={listboxId}
          role="listbox"
          className="absolute z-10 mt-1 w-full overflow-hidden rounded-lg border border-secondary bg-primary shadow-lg"
        >
          {search.isLoading && (
            <div className="px-3 py-2 text-sm text-tertiary">Searching…</div>
          )}
          {!search.isLoading && search.isError && (
            <div className="px-3 py-2 text-sm text-error-primary">
              Search failed. Try again.
            </div>
          )}
          {!search.isLoading && !search.isError && results.length === 0 && (
            <div className="px-3 py-2 text-sm text-tertiary">No users match.</div>
          )}
          {!search.isLoading && !search.isError &&
            results.map((user, index) => {
              const isActive = index === activeIndex;
              return (
                <button
                  key={user.id}
                  id={buildOptionId(listboxId, index)}
                  type="button"
                  role="option"
                  aria-selected={isActive}
                  onClick={() => handleSelect(user)}
                  // Keep mouse + keyboard in agreement: hovering an option
                  // promotes it to the active descendant so a subsequent Enter
                  // selects the right row.
                  onMouseEnter={() => setActiveIndex(index)}
                  className={cx(
                    "flex w-full flex-col items-start gap-0.5 px-3 py-2 text-left text-sm hover:bg-primary_hover",
                    isActive && "bg-primary_hover",
                  )}
                >
                  <span className="font-medium text-primary">
                    {user.displayName || user.email}
                  </span>
                  {user.displayName && (
                    <span className="text-xs text-tertiary">{user.email}</span>
                  )}
                </button>
              );
            })}
        </div>
      )}
    </div>
  );
}
