import { useId, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { SearchLg, ChevronDown } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import { Checkbox } from "@/components/base/checkbox/checkbox";
import { Select } from "@/components/base/select/select";
import { MultiSelect } from "@/components/base/multi-select/multi-select";
import { cx } from "@/lib/utils/cx";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "@/lib/list/filters/types";

interface FilterBarProps {
  specs: FilterSpec[];
  urlState: Pick<
    ListUrlState<string, string, string, string>,
    "textFilters" | "booleanFilters" | "multiFilters" | "setFilters"
  >;
}

/**
 * Standard list-filter surface (ADR-0107). Collapsible "Filters" panel.
 *
 * Submit-driven AND zero-cost while typing: the text/boolean controls are
 * **uncontrolled** (native DOM), so keystrokes never trigger a React render —
 * the page and its table are untouched until the user acts. The committed values
 * are read from the form (FormData) and pushed to the URL only on an explicit
 * action: the Search button (`onClick`) or Enter in a text input (`onKeyDown`).
 * Native form submission isn't relied on — react-aria's Input/Button don't emit
 * it — so both paths call `commit()` directly. Each control is keyed by its
 * committed value so external changes (back/forward, shared link, Clear all)
 * re-seed it. Builds `text`, `boolean`, `single-select`, and `multi-select`
 * controls; other types (`date-range`) throw so misuse fails loudly until
 * they are implemented.
 */
export function FilterBar({ specs, urlState }: FilterBarProps) {
  const [open, setOpen] = useState(true);
  const panelId = useId();
  const formRef = useRef<HTMLFormElement>(null);
  const { isActive, activeCount } = useListFilters(specs, urlState);
  const committedText = urlState.textFilters;
  const committedBool = urlState.booleanFilters;
  const committedMulti = urlState.multiFilters;

  // Read the uncontrolled controls' current values and commit them to the URL in
  // ONE navigation (see setFilters' note — looped setParams calls clobber each
  // other). Called from the Search button and from Enter — never per keystroke.
  const commit = () => {
    const form = formRef.current;
    if (!form) return;
    const data = new FormData(form);
    const text: Record<string, string> = {};
    const booleans: Record<string, boolean> = {};
    const multi: Record<string, string[]> = {};
    for (const s of specs) {
      if (s.type === "text" || s.type === "single-select") {
        text[s.key] = String(data.get(s.key) ?? "");
      } else if (s.type === "boolean") {
        booleans[s.key] = data.get(s.key) != null;
      } else if (s.type === "multi-select") {
        multi[s.key] = data.getAll(s.key).map(String);
      }
    }
    urlState.setFilters({ text, booleans, multi });
  };

  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    commit();
  };

  const onInputKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      commit();
    }
  };

  const clearAll = () => {
    const text: Record<string, string> = {};
    const booleans: Record<string, boolean> = {};
    const multi: Record<string, string[]> = {};
    for (const s of specs) {
      if (s.type === "text" || s.type === "single-select") text[s.key] = "";
      else if (s.type === "boolean") booleans[s.key] = false;
      else if (s.type === "multi-select") multi[s.key] = [];
    }
    urlState.setFilters({ text, booleans, multi });
  };

  return (
    <div className="rounded-xl bg-primary ring-1 ring-secondary">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen(o => !o)}
        className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium text-secondary"
      >
        <span>Filters{isActive ? ` (${activeCount} active)` : ""}</span>
        <ChevronDown className={cx("size-4 text-fg-quaternary transition-transform", open && "rotate-180")} />
      </button>

      {open && (
        <form ref={formRef} id={panelId} role="search" className="border-t border-secondary" onSubmit={onSubmit}>
          {/* Controls body — inputs / checkboxes wrap here, separate from the action footer. */}
          <div className="flex flex-wrap items-center gap-3 px-4 py-3">
            {specs.map(spec => {
              if (spec.type === "text") {
                const committed = committedText[spec.key] ?? "";
                return (
                  // Keyed by the committed value: typing leaves the key unchanged
                  // (no remount, native-fast), while a commit / back-forward / Clear
                  // all changes it and re-seeds the uncontrolled input via defaultValue.
                  <div key={`${spec.key}:${committed}`} className="w-full sm:w-72">
                    <Input
                      name={spec.key}
                      defaultValue={committed}
                      aria-label={spec.label}
                      placeholder={spec.placeholder}
                      icon={SearchLg}
                      size="sm"
                      maxLength={128}
                      onKeyDown={onInputKeyDown}
                    />
                  </div>
                );
              }
              if (spec.type === "boolean") {
                const committed = committedBool?.[spec.key] ?? false;
                return (
                  <Checkbox
                    key={`${spec.key}:${committed}`}
                    name={spec.key}
                    defaultSelected={committed}
                    label={spec.label}
                  />
                );
              }
              if (spec.type === "single-select") {
                const committed = committedText[spec.key] ?? "";
                return (
                  <div key={`${spec.key}:${committed}`} className="w-full sm:w-56">
                    <Select
                      name={spec.key}
                      defaultSelectedKey={committed}
                      aria-label={spec.label}
                      options={spec.options}
                      size="sm"
                    />
                  </div>
                );
              }
              if (spec.type === "multi-select") {
                const committed = committedMulti?.[spec.key] ?? [];
                return (
                  // Keyed by the committed values so Clear all / back-forward re-seeds
                  // the uncontrolled control via defaultSelectedKeys.
                  <div key={`${spec.key}:${committed.join(",")}`} className="w-full sm:w-56">
                    <MultiSelect
                      name={spec.key}
                      defaultSelectedKeys={committed}
                      aria-label={spec.label}
                      options={spec.options}
                      placeholder={spec.placeholder}
                      size="sm"
                    />
                  </div>
                );
              }
              throw new Error(
                `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text + boolean + single-select + multi-select only)`,
              );
            })}
          </div>

          {/* Action footer — left-aligned beneath the controls so Search sits next to
              the (left-aligned) filter inputs, not across the full panel width. Search
              leads as the primary action; Clear all follows when filters are active. */}
          <div className="flex items-center gap-3 border-t border-secondary px-4 py-3">
            <Button type="button" size="sm" color="secondary" onClick={commit}>Search</Button>
            {isActive && (
              <Button type="button" size="sm" color="link-gray" onClick={clearAll}>Clear all</Button>
            )}
          </div>
        </form>
      )}
    </div>
  );
}
