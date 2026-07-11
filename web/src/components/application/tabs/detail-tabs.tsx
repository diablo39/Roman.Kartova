import { Children, isValidElement, useEffect, useMemo } from "react";
import type { ReactElement, ReactNode } from "react";
import { Tab as AriaTab, TabList, TabPanel, Tabs } from "react-aria-components";
import { useSearchParams } from "react-router-dom";
import { cx } from "@/lib/utils/cx";

interface DetailTabProps {
  /** Stable slug used as the `?tab=` value and the react-aria key. */
  id: string;
  /** Visible tab label. */
  label: string;
  children: ReactNode;
}

/** Marker element: never rendered directly — the parent reads its props. */
function DetailTab(_props: DetailTabProps): null {
  return null;
}

interface DetailTabsProps {
  "aria-label": string;
  children: ReactNode;
  /** URL query param backing the active tab. Default "tab". */
  paramName?: string;
}

function DetailTabsRoot({ "aria-label": ariaLabel, children, paramName }: DetailTabsProps) {
  const [params, setParams] = useSearchParams();
  const tabs = Children.toArray(children).filter(isValidElement) as ReactElement<DetailTabProps>[];

  if (tabs.length === 0) return null;

  const ids = useMemo(() => tabs.map((t) => t.props.id), [tabs]);
  const param: string = paramName || "tab";

  const raw = params.get(param);
  const selected = raw && ids.includes(raw) ? raw : ids[0];

  // Normalize a present-but-invalid ?tab to the resolved default (replace, no history spam).
  // Absent ?tab is left clean — selection defaults to the first tab without touching the URL.
  useEffect(() => {
    if (raw !== null && !ids.includes(raw)) {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set(param, ids[0]!);
          return next;
        },
        { replace: true },
      );
    }
  }, [raw, ids, param, setParams]);

  return (
    <Tabs
      selectedKey={selected}
      onSelectionChange={(key) =>
        setParams(
          (prev) => {
            const next = new URLSearchParams(prev);
            next.set(param, String(key));
            return next;
          },
          { replace: true },
        )
      }
    >
      <TabList aria-label={ariaLabel} className="flex gap-8 border-b border-secondary">
        {tabs.map((t) => (
          <AriaTab
            key={t.props.id}
            id={t.props.id}
            className={({ isSelected }) =>
              cx(
                "-mb-px cursor-pointer border-b-2 pb-3 text-sm outline-hidden transition-colors",
                isSelected
                  ? "border-brand font-semibold text-primary"
                  : "border-transparent font-medium text-tertiary hover:text-secondary",
              )
            }
          >
            {t.props.label}
          </AriaTab>
        ))}
      </TabList>
      {tabs.map((t) => (
        <TabPanel key={t.props.id} id={t.props.id} className="pt-6 outline-hidden">
          {t.props.children}
        </TabPanel>
      ))}
    </Tabs>
  );
}

export const DetailTabs = Object.assign(DetailTabsRoot, { Tab: DetailTab });
