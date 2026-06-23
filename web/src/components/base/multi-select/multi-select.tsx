// web/src/components/base/multi-select/multi-select.tsx
import { useState, type Ref } from "react";
import { Check, ChevronDown } from "@untitledui/icons";
import {
  DialogTrigger,
  Dialog,
  Button as AriaButton,
  Popover as AriaPopover,
  ListBox as AriaListBox,
  ListBoxItem as AriaListBoxItem,
  type Selection,
} from "react-aria-components";
import { cx } from "@/lib/utils/cx";

export interface MultiSelectOption {
  label: string;
  value: string;
}

export interface MultiSelectProps {
  /** Form field name. Selected values are mirrored into hidden inputs so they are
   *  captured by `FormData.getAll(name)` (the uncontrolled FilterBar commit path). */
  name: string;
  "aria-label"?: string;
  label?: string;
  options: MultiSelectOption[];
  /** Uncontrolled initial selection (option `value`s). */
  defaultSelectedKeys?: string[];
  placeholder?: string;
  size?: "sm" | "md";
  className?: string;
  ref?: Ref<HTMLDivElement>;
}

export const MultiSelect = ({
  name,
  label,
  options,
  defaultSelectedKeys,
  placeholder = "Select…",
  size = "sm",
  className,
  ref,
  ...props
}: MultiSelectProps) => {
  const [selected, setSelected] = useState<Set<string>>(() => new Set(defaultSelectedKeys ?? []));

  const onChange = (keys: Selection) => {
    // We never use the "all" sentinel (no select-all affordance) — treat it as empty.
    setSelected(keys === "all" ? new Set() : new Set([...keys].map(k => String(k))));
  };

  // Summary text: placeholder when empty, the single label when one, "N selected" otherwise.
  const summary =
    selected.size === 0
      ? placeholder
      : selected.size === 1
        ? (options.find(o => o.value === [...selected][0])?.label ?? `${selected.size} selected`)
        : `${selected.size} selected`;

  return (
    <div ref={ref} className={cx("flex w-full flex-col gap-1.5", className)}>
      {label && <span className="text-sm font-medium text-secondary">{label}</span>}
      <DialogTrigger>
        <AriaButton
          aria-label={props["aria-label"]}
          className={cx(
            "flex w-full cursor-pointer items-center justify-between gap-2 rounded-lg bg-primary text-primary shadow-xs ring-1 ring-primary outline-hidden transition-shadow duration-100 ease-linear ring-inset data-focus-visible:ring-2 data-focus-visible:ring-brand",
            size === "sm" ? "px-3 py-2 text-sm" : "px-3 py-2.5 text-md",
          )}
        >
          <span className={cx("truncate", selected.size === 0 && "text-placeholder")}>{summary}</span>
          <ChevronDown aria-hidden className="size-4 shrink-0 text-fg-quaternary" />
        </AriaButton>
        <AriaPopover className="max-h-60 w-(--trigger-width) overflow-auto rounded-lg bg-primary py-1 shadow-lg ring-1 ring-secondary">
          <Dialog className="outline-hidden">
            <AriaListBox
              aria-label={props["aria-label"]}
              selectionMode="multiple"
              selectedKeys={selected}
              onSelectionChange={onChange}
              items={options}
              className="outline-hidden"
            >
              {(item: MultiSelectOption) => (
                <AriaListBoxItem
                  id={item.value}
                  textValue={item.label}
                  className="flex cursor-pointer items-center justify-between gap-2 px-3 py-2 text-sm text-primary outline-hidden select-none data-focused:bg-secondary data-selected:font-medium"
                >
                  {({ isSelected }: { isSelected: boolean }) => (
                    <>
                      <span className="truncate">{item.label}</span>
                      {isSelected && <Check aria-hidden className="size-4 shrink-0 text-fg-brand-primary" />}
                    </>
                  )}
                </AriaListBoxItem>
              )}
            </AriaListBox>
          </Dialog>
        </AriaPopover>
      </DialogTrigger>
      {/* Hidden inputs live OUTSIDE the portaled popover so they stay inside the
          enclosing <form>; FormData.getAll(name) then returns the selected values.
          Sorted only for deterministic test output — order is not semantically meaningful. */}
      {[...selected].sort().map(value => (
        <input key={value} type="hidden" name={name} value={value} />
      ))}
    </div>
  );
};

MultiSelect.displayName = "MultiSelect";
