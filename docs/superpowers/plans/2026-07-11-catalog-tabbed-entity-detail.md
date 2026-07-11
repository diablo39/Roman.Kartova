# Tabbed Entity-Detail Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generalize a tabbed detail-page layout across API/Service/Application and move the API spec render onto a dedicated Definition tab (E-11.F-02.S-04).

**Architecture:** A shared `<DetailTabs>` primitive wraps react-aria `Tabs`, owning `?tab=` URL sync and Untitled-UI styling. Each detail page declares its own tab set (`<DetailTabs.Tab id label>` children); the primitive derives valid ids from children, falls back to the first tab on unknown/absent `?tab`, and normalizes invalid deep-links. react-aria mounts only the active panel, so the Definition panel's lazy Scalar chunk stays unloaded until opened.

**Tech Stack:** React 19 + TypeScript, react-aria-components (ADR-0094), react-router `useSearchParams`, Vitest + RTL, Playwright (gate-10).

## Global Constraints

- Frontend-only. No backend/DB/auth/C# changes; no new endpoints, permissions, or codegen.
- Build gate: `npm run build` (`tsc -b`) must pass with 0 errors/warnings.
- UI stack: Untitled UI free-tier + react-aria-components only (ADR-0094). Use `cx` from `@/lib/utils/cx`.
- ADR-0084: every react-aria `<Table>` inside a panel keeps exactly one `isRowHeader` column; tab switch is a heavy re-render — verify no blank-page in a **real browser** (gate-10), jsdom recovers silently.
- Deep-link convention: active tab in `?tab=` query param; default tab `overview`; write with `{ replace: true }`.
- No empty tabs — a tab exists only if it has real content today.
- Tab order: Overview → Dependencies → Definition.

## Impact Analysis (codelens)

N/A — frontend-only slice. No C# symbol signatures or behavior change; nothing for `find_callers`/`find_references`/`analyze_change_impact` to analyze. The three detail-page components and their moved child sections are React/TSX only.

## File Structure

- Create: `web/src/components/application/tabs/detail-tabs.tsx` — the reusable primitive (`DetailTabs` + `DetailTabs.Tab`).
- Create: `web/src/components/application/tabs/__tests__/detail-tabs.test.tsx` — primitive unit tests.
- Modify: `web/src/features/catalog/pages/ApiDetailPage.tsx` — 3 tabs (Overview/Dependencies/Definition), spec → Definition.
- Modify: `web/src/features/catalog/pages/ServiceDetailPage.tsx` — 2 tabs (Overview/Dependencies).
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx` — 2 tabs (Overview/Dependencies).
- Modify: the three sibling `__tests__/*DetailPage.test.tsx` files — select the Dependencies tab where content moved.
- Create: `docs/architecture/decisions/ADR-0114-tabbed-entity-detail-layout.md` (content drafted in Task 5 for review).
- Modify: `docs/product/CHECKLIST.md` — mark E-11.F-02.S-04 done (Task 5, at close).

---

### Task 1: `DetailTabs` primitive

**Files:**
- Create: `web/src/components/application/tabs/detail-tabs.tsx`
- Test: `web/src/components/application/tabs/__tests__/detail-tabs.test.tsx`

**Interfaces:**
- Consumes: react-aria `Tabs/TabList/Tab/TabPanel`; react-router `useSearchParams`; `cx`.
- Produces:
  - `DetailTabs(props: { "aria-label": string; children: ReactNode; paramName?: string })` — default `paramName = "tab"`.
  - `DetailTabs.Tab(props: { id: string; label: string; children: ReactNode })` — marker element; renders `null` directly (its props are read by the parent).

- [ ] **Step 1: Write the failing test**

```tsx
// web/src/components/application/tabs/__tests__/detail-tabs.test.tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation } from "react-router-dom";
import { DetailTabs } from "../detail-tabs";

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="loc">{loc.search}</div>;
}

function renderTabs(initial = "/x") {
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <DetailTabs aria-label="Entity">
        <DetailTabs.Tab id="overview" label="Overview"><p>overview-body</p></DetailTabs.Tab>
        <DetailTabs.Tab id="dependencies" label="Dependencies"><p>deps-body</p></DetailTabs.Tab>
        <DetailTabs.Tab id="definition" label="Definition"><p>def-body</p></DetailTabs.Tab>
      </DetailTabs>
      <LocationProbe />
    </MemoryRouter>,
  );
}

describe("DetailTabs", () => {
  it("renders all tab labels and shows the first panel by default", () => {
    renderTabs();
    expect(screen.getByRole("tab", { name: "Overview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dependencies" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Definition" })).toBeInTheDocument();
    expect(screen.getByText("overview-body")).toBeInTheDocument();
    expect(screen.queryByText("deps-body")).not.toBeInTheDocument();
  });

  it("selects a tab on click and writes ?tab=", async () => {
    const user = userEvent.setup();
    renderTabs();
    await user.click(screen.getByRole("tab", { name: "Dependencies" }));
    expect(screen.getByText("deps-body")).toBeInTheDocument();
    expect(screen.getByTestId("loc").textContent).toContain("tab=dependencies");
  });

  it("honors an initial ?tab= deep-link", () => {
    renderTabs("/x?tab=definition");
    expect(screen.getByText("def-body")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Definition" })).toHaveAttribute("aria-selected", "true");
  });

  it("falls back to the first tab and normalizes an invalid ?tab=", () => {
    renderTabs("/x?tab=bogus");
    expect(screen.getByText("overview-body")).toBeInTheDocument();
    expect(screen.getByTestId("loc").textContent).toContain("tab=overview");
  });

  it("leaves the URL clean when no ?tab= is present", () => {
    renderTabs("/x");
    expect(screen.getByTestId("loc").textContent).toBe("");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/components/application/tabs/__tests__/detail-tabs.test.tsx`
Expected: FAIL — cannot resolve `../detail-tabs`.

- [ ] **Step 3: Write minimal implementation**

```tsx
// web/src/components/application/tabs/detail-tabs.tsx
import { Children, isValidElement, useEffect } from "react";
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

function DetailTabsRoot({ "aria-label": ariaLabel, children, paramName = "tab" }: DetailTabsProps) {
  const [params, setParams] = useSearchParams();
  const tabs = Children.toArray(children).filter(isValidElement) as ReactElement<DetailTabProps>[];
  const ids = tabs.map((t) => t.props.id);

  const raw = params.get(paramName);
  const selected = raw && ids.includes(raw) ? raw : ids[0];

  // Normalize a present-but-invalid ?tab to the resolved default (replace, no history spam).
  // Absent ?tab is left clean — selection defaults to the first tab without touching the URL.
  useEffect(() => {
    if (raw !== null && !ids.includes(raw)) {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set(paramName, ids[0]);
          return next;
        },
        { replace: true },
      );
    }
  }, [raw, ids, paramName, setParams]);

  return (
    <Tabs
      selectedKey={selected}
      onSelectionChange={(key) =>
        setParams(
          (prev) => {
            const next = new URLSearchParams(prev);
            next.set(paramName, String(key));
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/components/application/tabs/__tests__/detail-tabs.test.tsx`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/components/application/tabs/detail-tabs.tsx web/src/components/application/tabs/__tests__/detail-tabs.test.tsx
git commit -m "feat(web): DetailTabs primitive over react-aria Tabs with ?tab= URL sync (E-11.F-02.S-04)"
```

---

### Task 2: API detail page → Overview / Dependencies / Definition

**Files:**
- Modify: `web/src/features/catalog/pages/ApiDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`

**Interfaces:**
- Consumes: `DetailTabs` (Task 1); existing `ApiSpecSection({ api })`, `RelationshipsSection({ entityKind, entityId, entityTeamId, entityDisplayName, variant })`.

- [ ] **Step 1: Update the failing test** — Dependencies content (incoming relationships) now lives behind the Dependencies tab; deep-link it. Overview keeps name/style/version/spec-url.

```tsx
// web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx  (replace renderPage + the two its)
function renderPage(search = "") {
  return render(
    <MemoryRouter initialEntries={[`/catalog/apis/a1${search}`]}>
      <Routes><Route path="/catalog/apis/:id" element={<ApiDetailPage />} /></Routes>
    </MemoryRouter>,
  );
}

describe("ApiDetailPage", () => {
  it("shows Overview by default with name, style label, version and spec-url link", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: "Orders API" })).toBeInTheDocument();
    expect(screen.getByText("GraphQL")).toBeInTheDocument();
    expect(screen.getByText("v2")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /spec/i });
    expect(link).toHaveAttribute("href", "https://example.com/spec.json");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });

  it("exposes Overview, Dependencies and Definition tabs", () => {
    renderPage();
    expect(screen.getByRole("tab", { name: "Overview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dependencies" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Definition" })).toBeInTheDocument();
  });

  it("mounts a read-only incoming relationships section on the Dependencies tab", () => {
    renderPage("?tab=dependencies");
    expect(screen.getByText("Incoming")).toBeInTheDocument();
    expect(screen.queryByText("Outgoing")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`
Expected: FAIL — no `tab` roles (page not yet tabbed).

- [ ] **Step 3: Rewrite the page render with tabs**

Replace the `return (…)` block of `ApiDetailPage` (keep imports/loading/error/`api` resolution; add the `DetailTabs` import). New imports at top: `import { DetailTabs } from "@/components/application/tabs/detail-tabs";`

```tsx
  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{api.displayName}</h2>
          <Badge type="pill-color" color="gray" size="md">{API_STYLE_LABEL[api.style]}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <DetailTabs aria-label={api.displayName}>
          <DetailTabs.Tab id="overview" label="Overview">
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-tertiary">Description</h3>
                <p className="mt-1 text-sm text-secondary">
                  {api.description ? api.description : <span className="italic">No description</span>}
                </p>
              </section>
              <hr className="border-secondary" />
              <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="ID" value={api.id} mono />
                <Field label="Version" value={api.version} mono />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
                  <div className="mt-1 text-sm">
                    <Link to={`/teams/${api.teamId}`} className="text-primary hover:underline">
                      {teamNameById.get(api.teamId) ?? "View team"}
                    </Link>
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                  <div className="mt-1 text-sm"><CreatedByLink user={api.createdBy} /></div>
                </div>
                <Field label="Created" value={api.createdAt ? new Date(api.createdAt).toLocaleString() : "—"} />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Spec</div>
                  <div className="mt-1 text-sm">
                    {api.specUrl ? (
                      <a href={api.specUrl} target="_blank" rel="noopener noreferrer" className="text-primary hover:underline break-all">
                        View spec
                      </a>
                    ) : (
                      <span className="text-tertiary italic">No spec URL</span>
                    )}
                  </div>
                </div>
              </section>
            </div>
          </DetailTabs.Tab>

          <DetailTabs.Tab id="dependencies" label="Dependencies">
            <RelationshipsSection
              entityKind="api"
              entityId={api.id}
              entityTeamId={api.teamId}
              entityDisplayName={api.displayName}
              variant="incoming-only"
            />
          </DetailTabs.Tab>

          <DetailTabs.Tab id="definition" label="Definition">
            <ApiSpecSection api={api} />
          </DetailTabs.Tab>
        </DetailTabs>
      </CardContent>
    </Card>
  );
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ApiDetailPage.tsx web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx
git commit -m "feat(catalog): API detail page tabbed layout; spec on Definition tab (E-11.F-02.S-04)"
```

---

### Task 3: Service detail page → Overview / Dependencies

**Files:**
- Modify: `web/src/features/catalog/pages/ServiceDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx`

**Interfaces:**
- Consumes: `DetailTabs`; existing `ApiSurfaceSection({ entityKind, entityId })`, `DerivedDependenciesSection({ entityId })`, `DependencyMiniGraph({ entityKind, entityId, displayName })` (lazy), `RelationshipsSection`.

- [ ] **Step 1: Add/adjust tests** — Overview keeps description/metadata/endpoints table; Dependencies holds the moved sections. Include the ADR-0084 rowheader guard on the Dependencies tab (ApiSurfaceSection renders react-aria tables). Read the existing test file first and merge; the two new assertions to guarantee:

```tsx
// add inside describe("ServiceDetailPage", …)
  it("keeps the endpoints table on Overview with a row header", () => {
    renderPage(); // default overview
    expect(screen.getByRole("heading", { name: "Checkout Service" })).toBeInTheDocument();
    // endpoints table lives on Overview (default tab) — ADR-0084 guard
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });

  it("moves dependency + API-surface + relationships sections to the Dependencies tab", () => {
    renderPage("?tab=dependencies");
    expect(screen.getByRole("tab", { name: "Dependencies" })).toHaveAttribute("aria-selected", "true");
    // Relationships section header renders on the Dependencies tab
    expect(screen.getByText(/relationship/i)).toBeInTheDocument();
  });
```

Ensure `renderPage` accepts an optional search string and routes `"/catalog/services/:id"` (mirror the ApiDetailPage test harness; add `?${search}` to `initialEntries`). Keep the existing mocks for `useService`, `useTeamsList`, relationships, permissions, and add mocks for `useApiSurface`/derived-deps hooks if the existing suite doesn't already provide them (copy the empty-list mock shape from the current file).

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx`
Expected: FAIL — no `tab` roles yet.

- [ ] **Step 3: Rewrite the page render with tabs**

Add import `import { DetailTabs } from "@/components/application/tabs/detail-tabs";`. Replace the `return (…)`:

```tsx
  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{svc.displayName}</h2>
          <HealthBadge health={svc.health} size="md" />
        </div>
      </CardHeader>
      <CardContent>
        <DetailTabs aria-label={svc.displayName}>
          <DetailTabs.Tab id="overview" label="Overview">
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-tertiary">Description</h3>
                <p className="mt-1 text-sm text-secondary">
                  {svc.description ? svc.description : <span className="italic">No description</span>}
                </p>
              </section>
              <hr className="border-secondary" />
              <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="ID" value={svc.id} mono />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
                  <div className="mt-1 text-sm">
                    <Link to={`/teams/${svc.teamId}`} className="text-primary hover:underline">
                      {teamNameById.get(svc.teamId) ?? "View team"}
                    </Link>
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                  <div className="mt-1 text-sm"><CreatedByLink user={svc.createdBy} /></div>
                </div>
                <Field label="Created" value={svc.createdAt ? new Date(svc.createdAt).toLocaleString() : "—"} />
                <Field label="Version" value={svc.version} mono />
              </section>
              <hr className="border-secondary" />
              <section>
                <h3 className="text-sm font-medium text-tertiary">Endpoints</h3>
                {svc.endpoints.length === 0 ? (
                  <p className="mt-1 text-sm text-tertiary italic">No endpoints registered</p>
                ) : (
                  <div className="mt-2 overflow-hidden rounded-lg ring-1 ring-secondary">
                    <Table aria-label="Service endpoints">
                      <Table.Header>
                        <Table.Head id="url" isRowHeader>URL</Table.Head>
                        <Table.Head id="protocol">Protocol</Table.Head>
                      </Table.Header>
                      <Table.Body>
                        {svc.endpoints.map((e, i) => (
                          <Table.Row key={`${e.url}-${i}`} id={`${e.url}-${i}`}>
                            <Table.Cell className="font-mono text-sm text-primary">{e.url}</Table.Cell>
                            <Table.Cell className="text-sm">{PROTOCOL_LABEL[e.protocol]}</Table.Cell>
                          </Table.Row>
                        ))}
                      </Table.Body>
                    </Table>
                  </div>
                )}
              </section>
            </div>
          </DetailTabs.Tab>

          <DetailTabs.Tab id="dependencies" label="Dependencies">
            <div className="space-y-6">
              <ApiSurfaceSection entityKind="service" entityId={svc.id} />
              <hr className="border-secondary" />
              <Suspense fallback={<Skeleton className="h-80 w-full" />}>
                <DependencyMiniGraph entityKind="service" entityId={svc.id} displayName={svc.displayName} />
              </Suspense>
              <hr className="border-secondary" />
              <DerivedDependenciesSection entityId={svc.id} />
              <hr className="border-secondary" />
              <RelationshipsSection
                entityKind="service"
                entityId={svc.id}
                entityTeamId={svc.teamId}
                entityDisplayName={svc.displayName}
              />
            </div>
          </DetailTabs.Tab>
        </DetailTabs>
      </CardContent>
    </Card>
  );
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ServiceDetailPage.tsx web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx
git commit -m "feat(catalog): Service detail page tabbed layout (Overview/Dependencies) (E-11.F-02.S-04)"
```

---

### Task 4: Application detail page → Overview / Dependencies

**Files:**
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx`

**Interfaces:**
- Consumes: `DetailTabs`; existing `ApiSurfaceSection`, `DependencyMiniGraph` (lazy), `RelationshipsSection`; keeps `LifecycleMenu`, `AssignTeamPicker`, `EditApplicationDialog`, `SetSuccessorDialog`, `usePermissions` — all above the tabs / unchanged.

- [ ] **Step 1: Add/adjust tests** — Overview keeps description/metadata/successor; Dependencies holds mini-graph + api-surface + relationships. Merge into the existing suite (read it first; keep its permission/mocks). Guarantee:

```tsx
  it("shows Overview and Dependencies tabs; successor stays on Overview", () => {
    renderPage(); // default overview
    expect(screen.getByRole("tab", { name: "Overview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dependencies" })).toBeInTheDocument();
    // no Definition tab for applications
    expect(screen.queryByRole("tab", { name: "Definition" })).not.toBeInTheDocument();
  });

  it("renders relationships on the Dependencies tab", () => {
    renderPage("?tab=dependencies");
    expect(screen.getByText(/relationship/i)).toBeInTheDocument();
  });
```

Make `renderPage` accept an optional `search` appended to `initialEntries` (route `"/catalog/applications/:id"`).

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx`
Expected: FAIL — no `tab` roles yet.

- [ ] **Step 3: Rewrite the page render with tabs**

Add import `import { DetailTabs } from "@/components/application/tabs/detail-tabs";`. Keep the `<>…</>` fragment (header card + dialogs). Replace the inner `CardContent` body so the header stays in `CardHeader` (above tabs) and moved sections split across two tabs:

```tsx
      <Card>
        <CardHeader className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-baseline gap-3">
              <h2 className="text-2xl font-semibold text-primary">{app.displayName}</h2>
              {!permissionsLoading && (canForwardLifecycle || canReverseLifecycle) && (
                <LifecycleMenu
                  application={app}
                  canForward={canForwardLifecycle}
                  canReverse={canReverseLifecycle}
                  canOverride={canOverrideSunset}
                />
              )}
              <AssignTeamPicker applicationId={app.id} currentTeamId={app.teamId} />
            </div>
            {canEdit && (
              <Button color="secondary" size="sm" onClick={() => setEditOpen(true)}>Edit</Button>
            )}
          </div>
        </CardHeader>
        <CardContent>
          <DetailTabs aria-label={app.displayName}>
            <DetailTabs.Tab id="overview" label="Overview">
              <div className="space-y-6">
                <section>
                  <h3 className="text-sm font-medium text-tertiary">Description</h3>
                  <p className="mt-1 text-sm text-secondary">
                    {app.description ? app.description : <span className="italic">No description</span>}
                  </p>
                </section>
                <hr className="border-secondary" />
                <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <Field label="ID" value={app.id} mono />
                  <div>
                    <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                    <div className="mt-1 text-sm"><CreatedByLink user={app.createdBy} /></div>
                  </div>
                  <Field label="Created" value={app.createdAt ?? "—"} />
                </section>
                {(app.successorApplicationId || showSuccessorAction) && (
                  <>
                    <hr className="border-secondary" />
                    <section className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <div className="text-xs uppercase tracking-wide text-tertiary">Successor</div>
                        <div className="mt-1 text-sm">
                          {app.successorApplicationId ? (
                            <Link to={`/catalog/applications/${app.successorApplicationId}`} className="text-brand-secondary hover:underline">
                              {app.successorDisplayName ?? "—"} →
                            </Link>
                          ) : (
                            <span className="italic text-tertiary">None set</span>
                          )}
                        </div>
                      </div>
                      {showSuccessorAction && (
                        <Button color="secondary" size="sm" onClick={() => setSuccessorDialogOpen(true)}>
                          {app.successorApplicationId ? "Change successor" : "Set successor"}
                        </Button>
                      )}
                    </section>
                  </>
                )}
              </div>
            </DetailTabs.Tab>

            <DetailTabs.Tab id="dependencies" label="Dependencies">
              <div className="space-y-6">
                <ApiSurfaceSection entityKind="application" entityId={app.id} />
                <hr className="border-secondary" />
                <Suspense fallback={<Skeleton className="h-80 w-full" />}>
                  <DependencyMiniGraph entityKind="application" entityId={app.id} displayName={app.displayName} />
                </Suspense>
                <hr className="border-secondary" />
                <RelationshipsSection
                  entityKind="application"
                  entityId={app.id}
                  entityTeamId={app.teamId}
                  entityDisplayName={app.displayName}
                />
              </div>
            </DetailTabs.Tab>
          </DetailTabs>
        </CardContent>
      </Card>
```

(The `EditApplicationDialog` / `SetSuccessorDialog` blocks after `</Card>` stay unchanged.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ApplicationDetailPage.tsx web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx
git commit -m "feat(catalog): Application detail page tabbed layout (Overview/Dependencies) (E-11.F-02.S-04)"
```

---

### Task 5: ADR-0114, full web suite, CHECKLIST

**Files:**
- Create: `docs/architecture/decisions/ADR-0114-tabbed-entity-detail-layout.md`
- Modify: `docs/architecture/decisions/README.md` (keyword index row)
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Run the full web suite + typecheck build**

Run: `cd web && npm run build && npx vitest run`
Expected: build 0 errors/warnings; all suites green.

- [ ] **Step 2: Write ADR-0114** (Nygard template). Draft content — **review before saving** per working agreement:

```markdown
# ADR-0114 — Tabbed entity-detail layout

Status: Accepted
Date: 2026-07-11

## Context
Catalog entity-detail pages (API, Service, Application) had grown into long single-scroll cards: description + metadata + dependency mini-graph + API surface + relationships + (API only) an inline spec render. The OpenAPI/AsyncAPI spec render (Scalar, ~2.8 MB lazy chunk) sat at the bottom of the API page. Backstage and Compass both surface a component's API definition on a dedicated surface, not inline on an overview.

## Decision
Introduce a shared `DetailTabs` primitive (react-aria `Tabs`, ADR-0094) and split each detail page into tabs:
- Application / Service: Overview · Dependencies.
- API: Overview · Dependencies · Definition (spec render).
Conventions: active tab in `?tab=` (default `overview`, `replace` writes, invalid deep-link normalized to default); per-entity tab sets reflect real content only (no empty tabs); react-aria mounts only the active panel, so the Definition Scalar chunk loads lazily on open. The entity header (name, badges, action buttons) stays above the tab bar.

## Consequences
- Tab switch is a heavy re-render: every in-panel react-aria `<Table>` must keep exactly one `isRowHeader` column, verified in a real browser (ADR-0084) — jsdom recovers silently.
- Switching a tab re-mounts its panel (re-runs cheap, React-Query-cached section queries). `shouldForceMount` deliberately not used.
- Future per-entity surfaces (Documentation E-11.F-01, Deployments E-02.F-05, Settings) slot in as new tabs per feature.
- Amends the detail-page UI convention; supersedes nothing.
```

- [ ] **Step 3: Add the README keyword-index row** for ADR-0114 (follow the existing table format in `docs/architecture/decisions/README.md`).

- [ ] **Step 4: Update CHECKLIST** — mark `E-11.F-02.S-04` done with a one-line summary (tabbed layout across API/Service/Application, spec on Definition tab, ADR-0114, `?tab=` deep-link), noting FU-1 (Playwright E2E) deferred.

- [ ] **Step 5: Commit**

```bash
git add docs/architecture/decisions/ADR-0114-tabbed-entity-detail-layout.md docs/architecture/decisions/README.md docs/product/CHECKLIST.md
git commit -m "docs(catalog): ADR-0114 tabbed entity-detail layout + checklist (E-11.F-02.S-04)"
```

---

## Verification (gate-10, after all tasks)

Cold-start the dev server, log in, and in-SPA (ADR-0084):
- API detail: open Overview / Dependencies / Definition — confirm no blank-page, 0 console errors, Definition lazy-chunk loads only on open, spec renders read-only, empty-state when no spec.
- Service detail: Overview (endpoints table renders) / Dependencies (mini-graph + api-surface tables render, no blank-page).
- Application detail: Overview (successor) / Dependencies — open the Edit dialog while on each tab to exercise the ADR-0084 heavy re-render path.
- Deep-link `?tab=dependencies` and an invalid `?tab=bogus` (normalizes to Overview).
Commit screenshots under `docs/superpowers/verification/2026-07-11-tabbed-entity-detail/`. Convert the happy-path tab switch into a Playwright E2E spec (FU-1).

## Self-Review

- **Spec coverage:** DetailTabs primitive + URL sync (T1) ✓; API Definition tab (T2) ✓; Service tabs (T3) ✓; Application tabs (T4) ✓; per-entity tab mapping ✓; ADR-0084 rowheader guard (T3 test + gate-10) ✓; empty/error states (existing section states preserved; Definition empty-state via ApiSpecSection) ✓; ADR-0114 (T5) ✓; testing/gate profile (gate-10 section) ✓.
- **Placeholder scan:** none — all steps carry real code/commands. Tasks 3 & 4 instruct "read existing test first and merge" because those suites carry page-specific mocks not fully shown here; the guaranteed assertions are given verbatim.
- **Type consistency:** `DetailTabs` / `DetailTabs.Tab` prop names (`id`, `label`, `aria-label`, `paramName`) consistent across T1–T4; child-section signatures match verified exports (`ApiSpecSection({api})`, `ApiSurfaceSection({entityKind,entityId})`, `RelationshipsSection({…,variant})`, `DerivedDependenciesSection({entityId})`, `DependencyMiniGraph({entityKind,entityId,displayName})`).
