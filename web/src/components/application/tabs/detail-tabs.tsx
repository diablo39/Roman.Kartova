import { Children, isValidElement, useCallback, useEffect } from "react";
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
}

function DetailTabsRoot({ "aria-label": ariaLabel, children }: DetailTabsProps) {
  const [params, setParams] = useSearchParams();
  const tabs = Children.toArray(children).filter(isValidElement) as ReactElement<DetailTabProps>[];
  const ids = tabs.map((t) => t.props.id);
  const defaultId = ids[0];

  const raw = params.get("tab");
  const isKnown = raw !== null && ids.includes(raw);
  const selected = isKnown ? raw : defaultId;

  const setTab = useCallback(
    (value: string) =>
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set("tab", value);
          return next;
        },
        { replace: true },
      ),
    [setParams],
  );

  // Normalize a present-but-invalid ?tab to the resolved default (replace, no history spam).
  // Absent ?tab is left clean — selection defaults to the first tab without touching the URL.
  useEffect(() => {
    if (raw !== null && !isKnown && defaultId !== undefined) {
      setTab(defaultId);
    }
  }, [raw, isKnown, defaultId, setTab]);

  // After all hooks: call sites always pass ≥1 tab, but guard so an empty set can't
  // render an empty Tabs shell or push `undefined` into the URL.
  if (tabs.length === 0) return null;

  return (
    <Tabs selectedKey={selected} onSelectionChange={(key) => setTab(String(key))}>
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
