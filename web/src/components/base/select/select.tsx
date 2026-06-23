import type { Key, Ref } from "react";
import { ChevronDown } from "@untitledui/icons";
import {
  Select as AriaSelect,
  Button as AriaButton,
  Popover as AriaPopover,
  ListBox as AriaListBox,
  ListBoxItem as AriaListBoxItem,
  SelectValue,
} from "react-aria-components";
import { cx } from "@/lib/utils/cx";

export interface SelectOption {
  label: string;
  value: string;
}

export interface SelectProps {
  /** Form field name. react-aria renders a hidden form control so the selected
   *  option `value` is captured by `FormData` (the uncontrolled commit path). */
  name?: string;
  "aria-label"?: string;
  label?: string;
  options: SelectOption[];
  /** Uncontrolled initial selection (an option `value`). */
  defaultSelectedKey?: string;
  /** Controlled selection (an option `value`). */
  selectedKey?: string | null;
  onSelectionChange?: (key: Key | null) => void;
  placeholder?: string;
  size?: "sm" | "md";
  className?: string;
  ref?: Ref<HTMLDivElement>;
}

export const Select = ({
  name,
  label,
  options,
  defaultSelectedKey,
  selectedKey,
  onSelectionChange,
  placeholder = "Select…",
  size = "sm",
  className,
  ref,
  ...props
}: SelectProps) => {
  return (
    <AriaSelect
      ref={ref}
      name={name}
      aria-label={props["aria-label"]}
      defaultSelectedKey={defaultSelectedKey}
      selectedKey={selectedKey}
      onSelectionChange={onSelectionChange}
      placeholder={placeholder}
      className={cx("flex w-full flex-col gap-1.5", className)}
    >
      {label && <span className="text-sm font-medium text-secondary">{label}</span>}
      <AriaButton
        className={cx(
          "flex w-full cursor-pointer items-center justify-between gap-2 rounded-lg bg-primary text-primary shadow-xs ring-1 ring-primary outline-hidden transition-shadow duration-100 ease-linear ring-inset data-focus-visible:ring-2 data-focus-visible:ring-brand",
          size === "sm" ? "px-3 py-2 text-sm" : "px-3 py-2.5 text-md",
        )}
      >
        <SelectValue className="truncate data-placeholder:text-placeholder" />
        <ChevronDown aria-hidden className="size-4 shrink-0 text-fg-quaternary" />
      </AriaButton>
      <AriaPopover className="max-h-60 w-(--trigger-width) overflow-auto rounded-lg bg-primary py-1 shadow-lg ring-1 ring-secondary">
        <AriaListBox items={options} className="outline-hidden">
          {(item) => (
            <AriaListBoxItem
              id={item.value}
              textValue={item.label}
              className="flex cursor-pointer items-center px-3 py-2 text-sm text-primary outline-hidden select-none data-focused:bg-secondary data-selected:font-medium"
            >
              {item.label}
            </AriaListBoxItem>
          )}
        </AriaListBox>
      </AriaPopover>
    </AriaSelect>
  );
};

Select.displayName = "Select";
