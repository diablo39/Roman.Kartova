import { useId, useState } from "react";
import { SearchLg, ChevronDown } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import { Checkbox } from "@/components/base/checkbox/checkbox";
import { cx } from "@/lib/utils/cx";
import type { FilterSpec } from "@/lib/list/filters/types";
import type { useListFilters } from "@/lib/list/filters/useListFilters";

interface FilterBarProps {
  specs: FilterSpec[];
  filters: ReturnType<typeof useListFilters>;
}

/**
 * Standard list-filter surface (ADR-0107). Renders the controls inside a
 * collapsible "Filters" disclosure panel (expanded by default; the header keeps
 * the active count when collapsed so active filters are never hidden). Submit-
 * driven: text + boolean values are drafts until Enter or the Search button.
 * Builds the `text` and `boolean` controls; other types throw so misuse fails
 * loudly at dev time until they are implemented.
 */
export function FilterBar({ specs, filters }: FilterBarProps) {
  const [open, setOpen] = useState(true);
  const panelId = useId();

  return (
    <div className="rounded-xl bg-primary ring-1 ring-secondary">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen(o => !o)}
        className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium text-secondary"
      >
        <span>Filters{filters.isActive ? ` (${filters.activeCount} active)` : ""}</span>
        <ChevronDown className={cx("size-4 text-fg-quaternary transition-transform", open && "rotate-180")} />
      </button>

      {open && (
        <form
          id={panelId}
          role="search"
          className="flex flex-wrap items-center gap-3 border-t border-secondary px-4 py-3"
          onSubmit={(e) => { e.preventDefault(); filters.submit(); }}
        >
          {specs.map(spec => {
            if (spec.type === "text") {
              const { value, onChange } = filters.bind(spec.key);
              return (
                <div key={spec.key} className="w-full sm:w-72">
                  <Input
                    aria-label={spec.label}
                    placeholder={spec.placeholder}
                    icon={SearchLg}
                    size="sm"
                    value={value}
                    onChange={onChange}
                    maxLength={128}
                    onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); filters.submit(); } }}
                  />
                </div>
              );
            }
            if (spec.type === "boolean") {
              const { value, onChange } = filters.bindBoolean(spec.key);
              return <Checkbox key={spec.key} isSelected={value} onChange={onChange} label={spec.label} />;
            }
            throw new Error(
              `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text + boolean only)`,
            );
          })}

          <Button type="submit" size="sm" color="secondary">Search</Button>

          {filters.isActive && (
            <>
              <span className="text-sm text-tertiary">{filters.activeCount} active</span>
              <Button size="sm" color="link-gray" onClick={filters.clearAll}>Clear all</Button>
            </>
          )}
        </form>
      )}
    </div>
  );
}
