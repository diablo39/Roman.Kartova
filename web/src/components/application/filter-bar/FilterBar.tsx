import { SearchLg } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import type { FilterSpec } from "@/lib/list/filters/types";
import type { useListFilters } from "@/lib/list/filters/useListFilters";

interface FilterBarProps {
  specs: FilterSpec[];
  filters: ReturnType<typeof useListFilters>;
}

/**
 * Standard list-filter surface (ADR-0107). Renders each FilterSpec above the
 * DataTable. MVP builds the `text` control only; other types throw so misuse
 * fails loudly at dev time until they are implemented.
 */
export function FilterBar({ specs, filters }: FilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      {specs.map(spec => {
        if (spec.type !== "text") {
          throw new Error(
            `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text only)`,
          );
        }
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
            />
          </div>
        );
      })}

      {filters.isActive && (
        <Button size="sm" color="link-gray" onClick={filters.clearAll}>
          Clear all
        </Button>
      )}
    </div>
  );
}
